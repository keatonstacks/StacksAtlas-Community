using StacksAtlas.Core.Models;
using StacksAtlas.Core.Settings;
using StacksAtlas.Core.Services.Devices;
using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ModelUtils = StacksAtlas.Core.Models.NetworkUtils;
using ScanUtils = StacksAtlas.Core.Services.Scanning.NetworkUtils;

namespace StacksAtlas.Core.Services.Scanning
{
    public sealed class SubnetScanner(
        IHostPinger pinger,
        INmapService nmapService,
        NetworkSettingsStore settingsStore,
        ScanProgressService progressService,
        INetworkService networkService,
        INetworkInterfaceService networkInterfaceService,
        ISsdpDiscoveryService ssdpDiscoveryService,
        ILogger<SubnetScanner> logger)
    {
        private readonly IHostPinger _pinger = pinger;
        private readonly INmapService _nmapService = nmapService;
        private readonly NetworkSettingsStore _settingsStore = settingsStore;
        private readonly ScanProgressService _progressService = progressService;
        private readonly INetworkService _networkService = networkService;
        private readonly INetworkInterfaceService _networkInterfaceService = networkInterfaceService;
        private readonly ISsdpDiscoveryService _ssdpDiscoveryService = ssdpDiscoveryService;
        private readonly ILogger<SubnetScanner> _logger = logger;
        
        private static readonly bool IsLinuxOrMac = OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();

        public async Task<SubnetScanResult> ScanAsync(NetworkScope scope, CancellationToken token)
        {
            // 1. Pre-flight check
            token.ThrowIfCancellationRequested();

            var settings = _settingsStore.Load();
            var sw = Stopwatch.StartNew();
            var cidr = scope.Cidr;
            var scopeId = string.IsNullOrWhiteSpace(scope.Id) ? cidr : scope.Id;

            // Resolve dynamic local interface IP bound to this scope
            var resolvedInterfaceId = scope.ResolveInterfaceId(settings.InterfaceRoleMappings);
            string? localIpAddress = null;
            string? interfaceName = null;
            if (!string.IsNullOrEmpty(resolvedInterfaceId))
            {
                var nic = _networkInterfaceService.GetAllInterfaces().FirstOrDefault(i => i.Id == resolvedInterfaceId);
                localIpAddress = nic?.IpAddress;
                interfaceName = nic?.Name;
            }
            if (string.IsNullOrEmpty(localIpAddress))
            {
                localIpAddress = _networkInterfaceService.GetActiveInterface().IpAddress;
            }

            _logger.LogDebug(
                "Subnet scanner sweeping scope {Cidr} using bound local interface IP {LocalIp}{VlanSuffix}",
                cidr,
                localIpAddress,
                string.IsNullOrWhiteSpace(scope.VlanTag) ? string.Empty : $" (VLAN tag: {scope.VlanTag})");

            // OPTIMIZATION: Do not materialize the full list into memory to prevent OOM on /16 or /8 subnets.
            int totalHosts = 0;
            try
            {
                string[] parts = cidr.Split('/');
                if (parts.Length == 2 && int.TryParse(parts[1], out int prefix))
                {
                    totalHosts = prefix == 32 ? 1 : prefix == 31 ? 2 : (int)Math.Pow(2, 32 - prefix) - 2;
                }
                else 
                {
                    totalHosts = 1;
                }
            }
            catch (Exception ex)
            {
                return new SubnetScanResult(cidr, 0, 0, 0, [], $"Invalid CIDR: {ex.Message}");
            }

            if (totalHosts <= 0)
            {
                return new SubnetScanResult(cidr, 0, 0, 0, [], "CIDR resolved to zero IPs");
            }

            // Gather candidate IPs from active/passive discovery engines
            var candidateIps = new HashSet<string>();

            // UPnP/SSDP Discovery
            if (scope.EnableUpnp && !string.IsNullOrEmpty(localIpAddress))
            {
                _logger.LogDebug("SSDP engine active for scope {Cidr}", cidr);
                var upnpIps = await _ssdpDiscoveryService.DiscoverAsync(localIpAddress, TimeSpan.FromSeconds(2), token).ConfigureAwait(false);
                foreach (var ip in upnpIps)
                {
                    if (ScanUtils.IsIpInSubnet(ip, cidr))
                    {
                        candidateIps.Add(ip);
                    }
                }
            }

            // mDNS Cache Warmup
            if (scope.EnableMdns)
            {
                _logger.LogDebug("mDNS engine active for scope {Cidr}", cidr);
                await HostnameResolver.DiscoverAllMdnsAsync(token).ConfigureAwait(false);
            }

            // ARP Cache Inspection
            if (scope.EnableArp)
            {
                _logger.LogDebug("ARP engine active for scope {Cidr}", cidr);
                var arpNeighbors = await ModelUtils.GetPassiveNeighborsAsync(token).ConfigureAwait(false);
                foreach (var neighbor in arpNeighbors)
                {
                    if (ScanUtils.IsIpInSubnet(neighbor.Ip, cidr))
                    {
                        candidateIps.Add(neighbor.Ip);
                    }
                }
            }

            // Determine host list to scan
            IEnumerable<string> ipsToScan;
            if (scope.EnablePing)
            {
                ipsToScan = ScanUtils.GetIPsInSubnet(cidr);
            }
            else
            {
                ipsToScan = candidateIps;
            }

            // OPTIMIZATION: Use a dictionary for deduplication (Keyed by IP)
            var onlineResults = new ConcurrentDictionary<string, HostScanResult>();

            _progressService.StartScan(totalHosts, $"Scanning {cidr}");

            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = settings.MaxParallelPings,
                CancellationToken = token
            };

            try
            {
                var swPhase = Stopwatch.StartNew();
                await Parallel.ForEachAsync(ipsToScan, options, async (ip, ct) =>
                {
                    var result = await _pinger.PingAsync(ip, ct).ConfigureAwait(false);
                    if (result.IsOnline)
                    {
                        // If ARP is disabled, strip MAC attributes
                        if (!scope.EnableArp)
                        {
                            result = result with { Mac = null, Vendor = "Unknown" };
                        }

                        onlineResults.TryAdd(ip, result);
                        _progressService.UpdateStatus($"Found {ip}...");
                    }
                    
                    _progressService.ReportProgress(1);
                }).ConfigureAwait(false);
                swPhase.Stop();
                _logger.LogDebug("PERF: Raw Sweep took {Ms}ms", swPhase.ElapsedMilliseconds);

                // --- LAYER 4: PROXY ARP GHOST FILTER ---
                if (scope.EnableArp)
                {
                    swPhase.Restart();
                    _progressService.UpdateStatus("Filtering Proxy-ARP ghosts...", "Filtering");
                    var preFilterList = onlineResults.Values.ToList();
                    var macCounts = preFilterList
                        .Where(h => !string.IsNullOrEmpty(h.Mac))
                        .GroupBy(h => h.Mac!)
                        .ToDictionary(g => g.Key, g => g.Count());

                    var ignoredIps = new List<string>();
                    foreach (var h in preFilterList)
                    {
                        if (!string.IsNullOrEmpty(h.Mac) && macCounts[h.Mac] > 3 && HostPinger.IsLocallyAdministeredMac(h.Mac))
                        {
                            onlineResults.TryRemove(h.Ip, out _);
                            ignoredIps.Add(h.Ip);
                        }
                    }
                    
                    if (ignoredIps.Count > 0)
                    {
                        _logger.LogWarning("OUTLIER FILTER: Removed {Count} Proxy ARP ghosts. Ignored IPs: {Ips}", 
                            ignoredIps.Count, string.Join(", ", ignoredIps));
                    }
                    swPhase.Stop();
                }

                // --- GHOST-PING PROTECTION (SANITY FILTER) ---
                if (scope.EnablePing)
                {
                    var finalHostList = onlineResults.Values.ToList();
                    double onlineRatio = totalHosts > 0 ? (double)finalHostList.Count / totalHosts : 0;

                    if (totalHosts > 10 && onlineRatio > 0.85)
                    {
                        swPhase.Restart();
                        _progressService.UpdateStatus("Suspicious host count. Verifying ghosts...", "Verifying");
                        _logger.LogWarning("SUSPICIOUS SCAN: {Count}/{Total} hosts reported online. Triggering mandatory ghost-verification loop...", finalHostList.Count, totalHosts);
                        
                        var verifiedResults = new ConcurrentDictionary<string, HostScanResult>();
                        int verifiedCount = 0;

                        await Parallel.ForEachAsync(finalHostList, options, async (host, ct) =>
                        {
                            var verified = await _pinger.PingAsync(host.Ip, ct).ConfigureAwait(false);
                            if (verified.IsOnline)
                            {
                                if (!scope.EnableArp || !string.IsNullOrEmpty(host.Mac))
                                {
                                    verifiedResults[host.Ip] = host; 
                                }
                            }
                            Interlocked.Increment(ref verifiedCount);
                            _progressService.UpdateStatus($"Verifying {host.Ip} ({verifiedCount}/{finalHostList.Count})...");
                        }).ConfigureAwait(false);

                        onlineResults.Clear();
                        foreach (var kvp in verifiedResults)
                        {
                            onlineResults[kvp.Key] = kvp.Value;
                        }

                        var droppedIps = finalHostList.Select(h => h.Ip).Except(onlineResults.Keys).ToList();
                        swPhase.Stop();
                        _logger.LogInformation("GHOST FILTER COMPLETE: {Original} -> {Verified} hosts verified as truly online. Dropped Outliers: {Dropped} (took {Ms}ms)", 
                            finalHostList.Count, onlineResults.Count, string.Join(", ", droppedIps), swPhase.ElapsedMilliseconds);
                    }
                }
                
                _progressService.UpdateStatus("Finalizing sweep...", "Enriching");
            }
            catch (OperationCanceledException)
            {
                throw;
            }

            sw.Stop();
            _progressService.CompleteScan();

            var hostList = onlineResults.Values.ToList();
            
            _logger.LogDebug("Subnet {Cidr} scan complete. Found {Online}/{Total} hosts.", cidr, hostList.Count, totalHosts);
            
            var resolvedVlanTag = NetworkSettingsStore.ResolveDiscoveryVlanTag(
                settings, resolvedInterfaceId, scope.VlanTag);

            return new SubnetScanResult(
                Cidr: cidr,
                TotalHosts: totalHosts,
                OnlineHosts: hostList.Count,
                DurationMs: sw.ElapsedMilliseconds,
                Hosts: hostList,
                Error: null,
                DiscoveryScopeId: scopeId,
                DiscoveryInterfaceId: resolvedInterfaceId,
                DiscoveryInterfaceName: interfaceName,
                DiscoveryVlanTag: resolvedVlanTag
            );
        }
    }
}
