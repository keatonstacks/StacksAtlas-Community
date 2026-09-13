using StacksAtlas.Core.Models;

using StacksAtlas.Core.Services.Devices;
using StacksAtlas.Core.Services.Integrations;

using StacksAtlas.Core.Settings;

using System.Globalization;

using System.Net;

using System.Net.NetworkInformation;

using System.Net.Sockets;

using Microsoft.Extensions.Logging;



namespace StacksAtlas.Core.Services.Scanning;



public interface IHostPinger

{

    /// <param name="onDemand">User-initiated check  -  ICMP then port probe; never ARP-only liveness.</param>

    Task<HostScanResult> PingAsync(string ip, CancellationToken token, bool onDemand = false);

}



public sealed class HostPinger(

    NetworkSettingsStore settingsStore, 

    IntelligenceEngine intelligence,

    HttpTitleGrabber titleGrabber,

    INetworkService networkService,

    ILogger<HostPinger> logger) : IHostPinger

{

    private readonly NetworkSettingsStore _settingsStore = settingsStore;

    private readonly IntelligenceEngine _intelligence = intelligence;

    private readonly HttpTitleGrabber _titleGrabber = titleGrabber;

    private readonly INetworkService _networkService = networkService;

    private readonly ILogger<HostPinger> _logger = logger;

    

    private static readonly SemaphoreSlim _discoverySemaphore = new(512);

    private static readonly bool IsWindows = OperatingSystem.IsWindows();



    public async Task<HostScanResult> PingAsync(string ip, CancellationToken token, bool onDemand = false)

    {

        var settings = _settingsStore.Load();

        bool isLocal = _networkService.IsIpLocal(ip);

        

        if (!isLocal && (ip.StartsWith("192.168.") || ip.StartsWith("10.") || ip.StartsWith("172.1")))

            isLocal = true;



        int effectiveTimeout = isLocal ? settings.PingTimeoutMs : Math.Min(settings.PingTimeoutMs, 120);

        

        bool icmpSuccess = false;

        long roundtripTime = -1;



        var reply = await PerformPingAsync(ip, effectiveTimeout, token).ConfigureAwait(false);
        icmpSuccess = reply?.Status == IPStatus.Success;
        roundtripTime = reply?.RoundtripTime ?? -1;



        // --- TRANSPORT LIVENESS: ICMP and/or open port / HTTP (never ARP-only) ---

        List<int> openPorts = [];

        string? webTitle = null;



        if (!icmpSuccess || !onDemand)

        {

            openPorts = await PortScanner.ScanInterestingPortsAsync(ip, token).ConfigureAwait(false);

            if (openPorts.Count > 0)

            {

                int webPort = 0;

                if (openPorts.Contains(443)) webPort = 443;

                else if (openPorts.Contains(80)) webPort = 80;

                else if (openPorts.Contains(8443)) webPort = 8443;

                else if (openPorts.Contains(8080)) webPort = 8080;



                if (webPort > 0)
                    webTitle = await _titleGrabber.GetTitleAsync(ip, webPort, token).ConfigureAwait(false);

                if (openPorts.Contains(8080) || openPorts.Contains(8443))
                {
                    var probePort = openPorts.Contains(8443) ? 8443 : 8080;
                    if (!OpenAvcHostProbe.LooksLikeOpenAvcTitle(webTitle))
                    {
                        var openAvc = await OpenAvcHostProbe.ProbeAsync(ip, probePort, token).ConfigureAwait(false);
                        if (openAvc != null)
                            webTitle = openAvc.Version == "unknown"
                                ? "OpenAVC Room Control"
                                : $"OpenAVC {openAvc.Version}";
                    }
                }
            }
        }

        bool transportLive = icmpSuccess || openPorts.Count > 0 || !string.IsNullOrWhiteSpace(webTitle);

        if (!transportLive)

        {

            _logger.LogDebug("LIVENESS [{Ip}]: offline  -  no ICMP reply, open port, or HTTP response", ip);

            return HostScanResult.Offline(ip);

        }



        // --- LAYER-2 IDENTITY (ARP baseline  -  enriches live hosts, does not prove liveness) ---

        string? macNormalized = null;

        if (isLocal)

        {

            if (icmpSuccess)

            {

                // macOS ARP entries often lag ICMP; give the kernel a moment to populate.
                if (!IsWindows)
                    await Task.Delay(OperatingSystem.IsMacOS() ? 160 : 80, token).ConfigureAwait(false);

                macNormalized = await _networkService.GetMacAddressAsync(ip, token).ConfigureAwait(false);

            }

            else

            {

                macNormalized = await _networkService.GetMacAddressAsync(ip, token, activeOnly: true).ConfigureAwait(false);

            }

        }



        string? hostname = null;

        await _discoverySemaphore.WaitAsync(token).ConfigureAwait(false);

        try

        {

            hostname = HostnameResolver.ResolveFromCache(ip);

            hostname ??= await HostnameResolver.TryReverseDnsAsync(ip).ConfigureAwait(false);

        }

        finally

        {

            _discoverySemaphore.Release();

        }



        string? rawMac = macNormalized;

        string macForOui = string.Empty;



        bool isLocallyAdministered = !string.IsNullOrWhiteSpace(rawMac) && IsLocallyAdministeredMac(rawMac);

        bool hasValidMacFinal = !string.IsNullOrWhiteSpace(rawMac) 

            && rawMac != "000000000000"

            && !isLocallyAdministered;



        if (!hasValidMacFinal && !isLocallyAdministered)

        {

            if (_networkService.IsIpSelf(ip))

            {

                // Self  -  trusted

            }

            else if (icmpSuccess || openPorts.Count > 0 || !string.IsNullOrWhiteSpace(webTitle))

            {

                _logger.LogDebug("DISCOVERY [{Ip}]: MAC-less host  -  transport confirmed via {Method}",

                    ip, icmpSuccess ? "ICMP" : openPorts.Count > 0 ? "TCP" : "HTTP");

            }

            else

            {

                return HostScanResult.Offline(ip);

            }

            

            rawMac = null;

            macNormalized = null;

        }



        if (!string.IsNullOrWhiteSpace(rawMac) && rawMac.Length >= 12)

            macForOui = $"{rawMac[..2]}:{rawMac[2..4]}:{rawMac[4..6]}:{rawMac[6..8]}:{rawMac[8..10]}:{rawMac[10..12]}";



        string vendor = "Unknown Vendor";

        DeviceType ouiTypeHint = DeviceType.Unknown;



        if (isLocallyAdministered) 

            vendor = "Randomized MAC Address";

        else if (!string.IsNullOrWhiteSpace(macForOui))

        {

            var (v, t) = OuiDatabase.Lookup(macForOui);

            vendor = v;

            ouiTypeHint = t;

        }



        var hints = _intelligence.GetHints(
            name: webTitle ?? hostname,
            hostname: hostname,
            vendor: vendor,
            mac: macNormalized,
            ports: openPorts
        );

        if (OpenAvcHostProbe.LooksLikeOpenAvcTitle(webTitle))
        {
            hints.Add(new ClassificationHint(
                DeviceType.Network_Infrastructure,
                "OpenAVC",
                "Room Control Host",
                92,
                "OpenAvcProbe"));
        }



        if (ouiTypeHint != DeviceType.Unknown)

            hints.Add(new ClassificationHint(ouiTypeHint, vendor, null, 95, "OUI"));



        var (refinedType, refinedModel, refinedVendor, confidence, _) = _intelligence.Synthesize(hints);

        

        if (!string.IsNullOrEmpty(refinedVendor) && refinedVendor != "Unknown Vendor") 

            vendor = refinedVendor;



        string displayName = webTitle ?? hostname ?? ip;



        return new HostScanResult(

            ip,

            true,

            icmpSuccess ? roundtripTime : 0,

            macNormalized ?? string.Empty,

            Vendor: vendor,

            Hostname: hostname ?? string.Empty,

            Name: displayName,

            Type: refinedType.ToString(),

            Model: refinedModel,

            ConfidenceScore: confidence,

            OpenPorts: openPorts,

            Error: null,

            HttpTitle: webTitle

        );

    }



    private static async Task<PingReply?> PerformPingAsync(string ip, int timeoutMs, CancellationToken token)

    {

        try

        {

            using var ping = new Ping();

            if (IPAddress.TryParse(ip, out var address))

            {

                var timeout = TimeSpan.FromMilliseconds(timeoutMs);

                if (IsWindows)

                    return await ping.SendPingAsync(address, timeout, new byte[32], new PingOptions(), token).ConfigureAwait(false);

                return await ping.SendPingAsync(address, timeout, Array.Empty<byte>(), null, token).ConfigureAwait(false);

            }

        }

        catch (PingException) { }

        catch (SocketException) { }

        catch (PlatformNotSupportedException) { }

        catch (OperationCanceledException) { }

        return null;

    }



    internal static bool IsLocallyAdministeredMac(string mac)

    {

        if (string.IsNullOrWhiteSpace(mac) || mac.Length < 2) return false;



        string clean = mac.Replace(":", "").Replace("-", "").ToUpperInvariant();

        if (clean.Length < 2) return false;



        if (!byte.TryParse(clean[..2], NumberStyles.HexNumber, null, out byte firstOctet))

            return false;



        if ((firstOctet & 0x02) == 0) return false;



        if (clean.Length >= 6)

        {

            string oui = clean[..6];

            if (oui is "0A0027" or "525400" or "020042" or "D2462D" or "5254FF")

                return false;

        }



        return true;

    }

}


