using Microsoft.Extensions.Logging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Diagnostics;
using ArpLookup;
using StacksAtlas.Core.Services.Devices;

namespace StacksAtlas.Core.Services.Scanning;

public sealed class NetworkService : INetworkService
{
    private readonly ILogger<NetworkService> _logger;
    private readonly INetworkInterfaceService _interfaceService;
    private readonly global::System.Threading.Lock _cacheLock = new();
    private HashSet<string> _localIpCache = new();
    private List<string> _localSubnetCache = new();
    private DateTime _lastCacheUpdate = DateTime.MinValue;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private string? _detectedLocalIp;
    private string? _detectedLocalMac;
    private List<(string Ip, string Mac)> _passiveCache = new();
    private DateTime _lastPassiveUpdate = DateTime.MinValue;
    private static readonly SemaphoreSlim _passiveScanSemaphore = new(1, 1);

    public NetworkService(ILogger<NetworkService> logger, INetworkInterfaceService interfaceService)
    {
        _logger = logger;
        _interfaceService = interfaceService;
        RefreshCache();
    }

    private void RefreshCache()
    {
        lock (_cacheLock)
        {
            if (DateTime.UtcNow - _lastCacheUpdate < CacheTtl && _localIpCache.Count > 0)
                return;

            var newCache = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            try
            {
                // Use the UI-aware interface service to find our primary identity
                var primary = _interfaceService.GetActiveInterface();
                _detectedLocalIp = primary.IpAddress;
                _detectedLocalCidr = primary.GetCidr();
                
                // Get the MAC for the active interface
                var ni = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n => n.Id == primary.Id);
                _detectedLocalMac = ni?.GetPhysicalAddress()?.ToString().Replace("-", "").Replace(":", "").ToUpperInvariant() ?? string.Empty;

                // Populate the "Self" cache with ALL local IPs for O(1) checks
                var allInterfaces = _interfaceService.GetAllInterfaces();
                var newSubnets = new List<string>();

                foreach (var active in allInterfaces)
                {
                    newSubnets.Add(active.GetCidr());
                    newCache.Add(active.IpAddress);
                }

                _localIpCache = newCache;
                _localSubnetCache = newSubnets;
                _lastCacheUpdate = DateTime.UtcNow;

                _logger.LogDebug("Network Cache Refreshed. Primary: {Ip}, MAC: {Mac}, Self-Count: {Count}, Subnets: {SubCount}", 
                    _detectedLocalIp, _detectedLocalMac, _localIpCache.Count, _localSubnetCache.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to refresh network interface cache.");
            }
        }
    }

    public string GetLocalIpAddress()
    {
        RefreshCache();
        return _detectedLocalIp ?? "127.0.0.1";
    }

    public string GetLocalMacAddress()
    {
        RefreshCache();
        return _detectedLocalMac ?? string.Empty;
    }

    private string? _detectedLocalCidr;

    public string GetScannerCidr()
    {
        RefreshCache();
        if (string.IsNullOrWhiteSpace(_detectedLocalCidr)
            || string.Equals(_detectedLocalCidr, "N/A", StringComparison.OrdinalIgnoreCase))
        {
            return "192.168.1.0/24";
        }

        return _detectedLocalCidr;
    }

    public bool IsIpSelf(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return false;
        RefreshCache();
        return _localIpCache.Contains(ip) || ip == "127.0.0.1" || ip == "localhost";
    }

    public bool IsIpLocal(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return false;
        RefreshCache(); // Thread-safe fast exit if not expired
        
        if (IsIpSelf(ip)) return true;
        return _localSubnetCache.Any(s => NetworkUtils.IsIpInSubnet(ip, s));
    }

    public bool IsIpInSubnet(string ipAddress, string cidr)
    {
        return NetworkUtils.IsIpInSubnet(ipAddress, cidr);
    }

    public async Task<List<(string Ip, string Mac)>> GetPassiveNeighborsAsync(CancellationToken token)
    {
        lock (_cacheLock)
        {
            if (DateTime.UtcNow - _lastPassiveUpdate < TimeSpan.FromSeconds(10) && _passiveCache.Count > 0)
                return _passiveCache;
        }

        // Prevent concurrent ARP scans
        await _passiveScanSemaphore.WaitAsync(token);
        try
        {
            // Double-check the cache after acquiring the semaphore
            lock (_cacheLock)
            {
                if (DateTime.UtcNow - _lastPassiveUpdate < TimeSpan.FromSeconds(10) && _passiveCache.Count > 0)
                    return _passiveCache;
            }

            var results = new List<(string Ip, string Mac)>();
            try
            {
                bool success = await TryPopulatePassiveArpFromCommandAsync(results, token).ConfigureAwait(false);

                if (!success && (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()))
                {
                    TryPopulatePassiveArpFromProcNetArp(results);
                }

                int locallyAdministeredCount = results.Count(r => HostPinger.IsLocallyAdministeredMac(r.Mac));
                if (locallyAdministeredCount > 0)
                {
                    _logger.LogDebug("ARP Cache: {Count} locally-administered MAC entries found (randomized/virtual devices - filtered from discovery)", locallyAdministeredCount);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning("Passive ARP lookup failed: {Message}", ex.Message);
            }

            lock (_cacheLock)
            {
                _passiveCache = results;
                _lastPassiveUpdate = DateTime.UtcNow;
            }

            return results;
        }
        finally
        {
            _passiveScanSemaphore.Release();
        }
    }

    private static async Task<bool> TryPopulatePassiveArpFromCommandAsync(List<(string Ip, string Mac)> results, CancellationToken token)
    {
        string command = OperatingSystem.IsWindows() ? "arp" : "arp";
        string args = OperatingSystem.IsWindows() ? "-a" : "-an";

        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = args,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            process.Start();

            using var ctr = token.Register(() => { try { process.Kill(); } catch { } });

            string output = await process.StandardOutput.ReadToEndAsync(token).ConfigureAwait(false);
            await process.WaitForExitAsync(token).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(output))
                return false;

            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var ipMatch = global::System.Text.RegularExpressions.Regex.Match(line, @"(\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})");
                var macMatch = global::System.Text.RegularExpressions.Regex.Match(line, @"([0-9a-fA-F]{1,2}[:\-][0-9a-fA-F]{1,2}[:\-][0-9a-fA-F]{1,2}[:\-][0-9a-fA-F]{1,2}[:\-][0-9a-fA-F]{1,2}[:\-][0-9a-fA-F]{1,2})");

                if (ipMatch.Success && macMatch.Success)
                {
                    if (DeviceMacNormalizer.TryParse(macMatch.Value, out var mac))
                    {
                        results.Add((ipMatch.Value, mac));
                    }
                }
            }

            return results.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    private static void TryPopulatePassiveArpFromProcNetArp(List<(string Ip, string Mac)> results)
    {
        const string procPath = "/proc/net/arp";
        if (!File.Exists(procPath))
            return;

        try
        {
            var lines = File.ReadAllLines(procPath);
            foreach (var line in lines.Skip(1))
            {
                var columns = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (columns.Length < 4) continue;

                var ip = columns[0];
                if (IPAddress.TryParse(ip, out _) && DeviceMacNormalizer.TryParse(columns[3], out var mac))
                {
                    results.Add((ip, mac));
                }
            }
        }
        catch { }
    }

    public async Task<string?> GetMacAddressAsync(string ipAddress, CancellationToken token, bool activeOnly = false)
    {
        try
        {
            if (!IPAddress.TryParse(ipAddress, out var address)) return null;

            // 1. Passive Check (Fast)  -  skipped for liveness probes (stale ARP after disconnect)
            if (!activeOnly)
            {
                var passive = await GetPassiveNeighborsAsync(token);
                var cached = passive.FirstOrDefault(p =>
                    string.Equals(p.Ip, ipAddress, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(cached.Mac)) return cached.Mac;
            }

            // 2. Active Check (Authoritative)
            var physicalAddress = await Arp.LookupAsync(address);
            var activeMacRaw = physicalAddress?.ToString();
            if (DeviceMacNormalizer.TryParse(activeMacRaw, out var activeMac))
            {
                if (HostPinger.IsLocallyAdministeredMac(activeMac))
                {
                    _logger.LogDebug("ARP Active: Locally-administered MAC {Mac} for {Ip}  -  allowing through (phone/randomized)", activeMac, ipAddress);
                }
                return activeMac;
            }

            // 3. macOS/Linux: read kernel ARP entry populated by the ping we just sent.
            var systemArpMac = await TryGetMacFromSystemArpAsync(ipAddress, token).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(systemArpMac))
                return systemArpMac;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogTrace("Active ARP lookup failed for {Ip}: {Message}", ipAddress, ex.Message);
        }

        return null;
    }

    private async Task<string?> TryGetMacFromSystemArpAsync(string ipAddress, CancellationToken token)
    {
        if (OperatingSystem.IsWindows())
            return await TryGetMacFromArpProcessAsync("arp", $"-a {ipAddress}", ipAddress, token).ConfigureAwait(false);

        // macOS/BSD: ARP entries often appear a beat after ICMP. Retry with backoff and
        // fall back to full-table parse (`arp -an`) which is more reliable than `arp -n <ip>`.
        var attempts = OperatingSystem.IsMacOS() ? 5 : 3;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            var mac = await TryGetMacFromArpProcessAsync("arp", $"-n {ipAddress}", ipAddress, token).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(mac))
                return mac;

            // macOS also accepts `arp <ip>` without -n.
            if (OperatingSystem.IsMacOS())
            {
                mac = await TryGetMacFromArpProcessAsync("arp", ipAddress, ipAddress, token).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(mac))
                    return mac;
            }

            mac = await TryGetMacFromFullArpTableAsync(ipAddress, token).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(mac))
                return mac;

            if (attempt < attempts)
                await Task.Delay(OperatingSystem.IsMacOS() ? 80 * attempt : 40, token).ConfigureAwait(false);
        }

        return null;
    }

    private static async Task<string?> TryGetMacFromFullArpTableAsync(string ipAddress, CancellationToken token)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "arp",
                Arguments = OperatingSystem.IsMacOS() ? "-an" : "-an",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            process.Start();
            using var ctr = token.Register(() => { try { process.Kill(); } catch { } });
            var output = await process.StandardOutput.ReadToEndAsync(token).ConfigureAwait(false);
            await process.WaitForExitAsync(token).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(output))
                return null;

            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!line.Contains(ipAddress, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (line.Contains("(incomplete)", StringComparison.OrdinalIgnoreCase))
                    return null;
                if (DeviceMacNormalizer.TryParse(line, out var mac))
                    return mac;
            }

            return null;
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            return null;
        }
    }

    private static async Task<string?> TryGetMacFromArpProcessAsync(
        string command, string args, string ipAddress, CancellationToken token)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = args,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            process.Start();
            using var ctr = token.Register(() => { try { process.Kill(); } catch { } });
            var output = await process.StandardOutput.ReadToEndAsync(token).ConfigureAwait(false);
            await process.WaitForExitAsync(token).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(output) || output.Contains("(incomplete)", StringComparison.OrdinalIgnoreCase))
                return null;

            // Prefer a line that mentions the target IP (macOS prints multi-line noise rarely).
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!line.Contains(ipAddress, StringComparison.OrdinalIgnoreCase) && output.Contains(ipAddress, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (DeviceMacNormalizer.TryParse(line, out var lineMac))
                    return lineMac;
            }

            if (DeviceMacNormalizer.TryParse(output, out var mac))
                return mac;

            return null;
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            return null;
        }
    }

    public async Task<bool> ProbeArpAsync(string ipAddress, CancellationToken token)
    {
        try
        {
            if (!IPAddress.TryParse(ipAddress, out var address)) return false;
            var physicalAddress = await Arp.LookupAsync(address);
            return physicalAddress != null && !HostPinger.IsLocallyAdministeredMac(physicalAddress.ToString());
        }
        catch { return false; }
    }
}
