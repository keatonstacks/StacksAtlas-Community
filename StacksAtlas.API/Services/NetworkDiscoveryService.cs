using Microsoft.Extensions.Logging;
using StacksAtlas.Core.Abstractions;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Services.Scanning;
using StacksAtlas.Core.Services.Network;
using StacksAtlas.Core.Services.Devices;
using System.Diagnostics;

namespace StacksAtlas.API.Services;

public interface INetworkDiscoveryService
{
    Task<SweepResult> RunDiscoveryCycleAsync(CancellationToken ct);
}

public class NetworkDiscoveryService(
    ILogger<NetworkDiscoveryService> logger,
    ISweepMetricsCollector collector,
    ISnmpService snmpService,
    INetworkService networkService,
    IClock clock) : INetworkDiscoveryService
{
    public async Task<SweepResult> RunDiscoveryCycleAsync(CancellationToken ct)
    {
        var swTotal = Stopwatch.StartNew();
        
        // 1. Discovery Phase (mDNS)
        var swPhase = Stopwatch.StartNew();
        await HostnameResolver.DiscoverAllMdnsAsync(ct);
        logger.LogDebug("PERF: Phase 1 (mDNS) took {Ms}ms", swPhase.ElapsedMilliseconds);

        // 2. Network Sweep
        swPhase.Restart();
        var sweep = await collector.RunSweepAsync(ct);
        logger.LogDebug("PERF: Phase 2 (Sweep) took {Ms}ms", swPhase.ElapsedMilliseconds);

        // 3. Self-Injection (Host Node)
        InjectHostNode(sweep);
        logger.LogDebug("Discovery: After self-injection, total online hosts: {Count}", sweep.TotalOnline);

        // 4. Enrichment (SNMP, etc.)
        swPhase.Restart();
        await EnrichHostsAsync(sweep, ct);
        logger.LogDebug("PERF: Phase 3 (Enrichment) took {Ms}ms", swPhase.ElapsedMilliseconds);

        swTotal.Stop();
        logger.LogDebug("Discovery cycle complete: {Online}/{Total} hosts in {Ms}ms",
            sweep.TotalOnline, sweep.TotalHosts, swTotal.ElapsedMilliseconds);

        return sweep;
    }

    private void InjectHostNode(SweepResult sweep)
    {
        var localIp = networkService.GetLocalIpAddress();
        var localMac = networkService.GetLocalMacAddress();
        
        var firstSubnet = sweep.Subnets.FirstOrDefault();
        if (firstSubnet == null) return;

        // Remove any imperfect existing record for ourselves
        var existing = firstSubnet.Hosts.FirstOrDefault(h => h.Ip == localIp || (!string.IsNullOrEmpty(localMac) && h.Mac == localMac));
        if (existing != null) firstSubnet.Hosts.Remove(existing);

        // Inject the authoritative "StacksAtlas Host" record
        firstSubnet.Hosts.Add(new HostScanResult(
            Ip: localIp,
            IsOnline: true,
            RoundtripTimeMs: 0,
            Mac: localMac,
            Vendor: "StacksAtlas",
            Hostname: Environment.MachineName,
            Name: "StacksAtlas Host Node",
            Type: "Network Infrastructure",
            Model: "StacksAtlas Host Node",
            ConfidenceScore: 100,
            OpenPorts: new List<int>(),
            Error: null
        ));
    }

    private async Task EnrichHostsAsync(SweepResult sweep, CancellationToken ct)
    {
        var tasks = new List<Task<HostScanResult>>();

        foreach (var subnet in sweep.Subnets)
        {
            foreach (var h in subnet.Hosts)
            {
                if (h.IsOnline && h.Ip != networkService.GetLocalIpAddress())
                {
                    tasks.Add(EnrichSingleHostAsync(h, ct));
                }
                else
                {
                    tasks.Add(Task.FromResult(h));
                }
            }
        }

        var results = await Task.WhenAll(tasks);

        // Update the subnets with the enriched results
        int resultIndex = 0;
        foreach (var subnet in sweep.Subnets)
        {
            var count = subnet.Hosts.Count;
            subnet.Hosts.Clear();
            for (int i = 0; i < count; i++)
            {
                subnet.Hosts.Add(results[resultIndex++]);
            }
        }
    }

    private async Task<HostScanResult> EnrichSingleHostAsync(HostScanResult h, CancellationToken ct)
    {
        try
        {
            var (sName, sDescr) = await snmpService.GetDeviceIdentityAsync(h.Ip);
            if (!string.IsNullOrEmpty(sName) || !string.IsNullOrEmpty(sDescr))
            {
                return h with 
                { 
                    SnmpSysName = sName, 
                    SnmpSysDescr = sDescr, 
                    SnmpLastCheck = clock.UtcNow 
                };
            }
        }
        catch (Exception ex)
        {
            logger.LogTrace(ex, "Enrichment failed for {Ip}", h.Ip);
        }
        return h;
    }
}
