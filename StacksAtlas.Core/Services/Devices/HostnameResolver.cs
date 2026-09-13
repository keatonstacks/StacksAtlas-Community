using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Zeroconf;

namespace StacksAtlas.Core.Services.Devices;

public static class HostnameResolver
{
    public static StacksAtlas.Core.Abstractions.IClock Clock { get; set; } = new StacksAtlas.Core.Abstractions.SystemClock();

    // ConcurrentDictionary is already thread-safe; no extra Lock needed.
    // Key: IP Address, Value: (Hostname, Timestamp)
    private static readonly ConcurrentDictionary<string, (string Name, DateTime Timestamp)> _cache = new();

    // Limits how often we blast the network with mDNS packets (every 2 minutes)
    private static DateTime _lastGlobalScan = DateTime.MinValue;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan GlobalScanInterval = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Performs a broadcast discovery for mDNS devices (Apple, Chromecast, Printers).
    /// Safe to call frequently; it internally throttles itself.
    /// </summary>
    public static async Task DiscoverAllMdnsAsync(CancellationToken token)
    {
        // 1. Throttle: Don't scan if we just did it recently
        if (Clock.UtcNow - _lastGlobalScan < GlobalScanInterval) return;

        try
        {
            // 2. Scan for protocols that reveal rich names
            string[] protocols = {
                // Consumer/IT Discovery
                "_http._tcp.local.",        // Web Interfaces (Printers, Routers)
                "_airplay._tcp.local.",     // Apple Devices
                "_googlecast._tcp.local.",  // Chromecasts / Smart TVs
                "_spotify-connect._tcp.",   // Speakers
                "_printer._tcp.local.",     // Printers
                
                // Pro-AV / Dante Discovery
                "_netaudio-arc._udp.local.",  // Dante Audio Routing Control
                "_netaudio-dcp._udp.local.",  // Dante Control Protocol
                "_netaudio-cmc._udp.local.",  // Dante Clock Master Control
                "_ptp._udp.local.",           // PTP Clock Sync (IEEE 1588)
                "_aes67._udp.local.",         // AES67 Audio-over-IP
                "_raop._tcp.local.",          // AirPlay Audio (Apple)
                "_crestron._tcp.local.",      // Crestron Devices
                "_qsys._tcp.local."           // Q-SYS Devices
            };

            // Run in parallel but respect the cancellation token
            var results = await ZeroconfResolver.ResolveAsync(protocols,
                scanTime: TimeSpan.FromMilliseconds(1000),
                retries: 1,
                retryDelayMilliseconds: 500,
                callback: null,
                cancellationToken: token);

            foreach (var result in results)
            {
                if (!string.IsNullOrWhiteSpace(result.DisplayName))
                {
                    // Clean up the name (e.g., "Bedroom TV.local." -> "Bedroom TV")
                    string cleanName = result.DisplayName.TrimEnd('.');
                    _cache[result.IPAddress] = (cleanName, Clock.UtcNow);
                }
            }

            _lastGlobalScan = Clock.UtcNow;
        }
        catch (OperationCanceledException) { /* Clean shutdown */ }
        catch (Exception) { /* Network errors shouldn't crash the scanner */ }
    }

    /// <summary>
    /// Instant lookup. Returns null if not in cache or if cache is expired.
    /// </summary>
    public static string? ResolveFromCache(string ip)
    {
        if (_cache.TryGetValue(ip, out var entry))
        {
            if (Clock.UtcNow - entry.Timestamp < CacheDuration)
            {
                return entry.Name == "NEGATIVE_CACHE" ? null : entry.Name;
            }
            // Expired
            _cache.TryRemove(ip, out _);
        }
        return null;
    }

    /// <summary>
    /// Tries mDNS first, then falls back to a timeout-protected Reverse DNS lookup, 
    /// and finally a NetBIOS probe.
    /// </summary>
    public static async Task<string?> TryReverseDnsAsync(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return null;

        // 1. Check Cache (Positive and Negative)
        if (_cache.TryGetValue(ip, out var entry))
        {
            if (Clock.UtcNow - entry.Timestamp < CacheDuration)
            {
                return entry.Name == "NEGATIVE_CACHE" ? null : entry.Name;
            }
            _cache.TryRemove(ip, out _);
        }

        try
        {
            // 2. DNS Lookup with explicit timeout protection
            var dnsTask = Dns.GetHostEntryAsync(ip);
            var completedTask = await Task.WhenAny(dnsTask, Task.Delay(800));

            if (completedTask == dnsTask)
            {
                var entryResult = await dnsTask;
                if (!string.IsNullOrWhiteSpace(entryResult.HostName) && entryResult.HostName != ip)
                {
                    string simpleName = entryResult.HostName.Split('.')[0];
                    _cache[ip] = (simpleName, Clock.UtcNow);
                    return simpleName;
                }
            }

            // 3. NetBIOS Probe (Port 137)
            // Very common for Windows/NAS/Printers on local networks
            var netbiosName = await ProbeNetBiosNameAsync(ip);
            if (!string.IsNullOrEmpty(netbiosName))
            {
                _cache[ip] = (netbiosName, Clock.UtcNow);
                return netbiosName;
            }
            
            // 4. NEGATIVE CACHE
            _cache[ip] = ("NEGATIVE_CACHE", Clock.UtcNow.AddMinutes(-5));
        }
        catch 
        { 
            _cache[ip] = ("NEGATIVE_CACHE", Clock.UtcNow.AddMinutes(-5));
        }

        return null;
    }

    private static async Task<string?> ProbeNetBiosNameAsync(string ip)
    {
        try
        {
            using var client = new UdpClient();
            client.Client.SendTimeout = 500;
            client.Client.ReceiveTimeout = 500;
            
            // NetBIOS Name Request packet
            byte[] packet = new byte[] {
                0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 
                0x20, 0x43, 0x4b, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 
                0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 
                0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x00, 0x00, 0x21, 
                0x00, 0x01 
            };

            await client.SendAsync(packet, packet.Length, ip, 137);
            
            // Wait for response with explicit timeout
            var receiveTask = client.ReceiveAsync();
            var result = await receiveTask.WaitAsync(TimeSpan.FromMilliseconds(500));
            
            if (result.Buffer.Length > 57)
            {
                int numberOfNames = result.Buffer[56];
                if (numberOfNames > 0)
                {
                    // The first name is usually the machine name
                    string name = Encoding.ASCII.GetString(result.Buffer, 57, 15).Trim();
                    return name;
                }
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Legacy wrapper for compatibility.
    /// </summary>
    public static async Task<string?> ResolveAsync(string ip) => await TryReverseDnsAsync(ip);
}
