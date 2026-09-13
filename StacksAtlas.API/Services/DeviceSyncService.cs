using Microsoft.Extensions.Logging;
using StacksAtlas.Core.Abstractions;
using StacksAtlas.Core.Data;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Services.Devices;
using StacksAtlas.Core.Services.Scanning;
using StacksAtlas.Core.Settings;
using StacksAtlas.Core.Services.Licensing;
using StacksAtlas.Core.Helpers;
using System.Collections.Concurrent;

namespace StacksAtlas.API.Services;

public interface IDeviceSyncService
{
    Task<(List<Device> Devices, Dictionary<Guid, string> PreSweepStatuses)> SyncSweepResultAsync(SweepResult sweep, CancellationToken ct);
}

public class DeviceSyncService(
    ILogger<DeviceSyncService> logger,
    IDeviceRepository repo,
    SweepHistoryRepository history,
    ILicenseService licenseService,
    LeaseHistoryRepository leaseRepo,
    SecurityAuditService securityAuditor,
    NetworkSettingsStore settingsStore,
    FederationSettingsStore federationSettingsStore,
    IEventRepository events,
    IClock clock) : IDeviceSyncService
{
    private readonly ILogger<DeviceSyncService> _logger = logger;
    private readonly IDeviceRepository _repo = repo;
    private readonly SweepHistoryRepository _history = history;
    private readonly ILicenseService _licenseService = licenseService;
    private readonly LeaseHistoryRepository _leaseRepo = leaseRepo;
    private readonly SecurityAuditService _securityAuditor = securityAuditor;
    private readonly NetworkSettingsStore _settingsStore = settingsStore;
    private readonly FederationSettingsStore _fedSettingsStore = federationSettingsStore;
    private readonly IEventRepository _events = events;
    private readonly IClock _clock = clock;

    private readonly ConcurrentDictionary<string, int> _strikeTracker = new();
    private readonly ConcurrentDictionary<string, int> _recoveryTracker = new();
    /// <summary>IPs that already logged Offline WRN since last recovery (suppress flap spam).</summary>
    private readonly ConcurrentDictionary<string, byte> _offlineLoggedIps = new();
    private static readonly TimeSpan WriteThrottle = TimeSpan.FromSeconds(30);
    private const int RECOVERY_THRESHOLD = 2;

    public async Task<(List<Device> Devices, Dictionary<Guid, string> PreSweepStatuses)> SyncSweepResultAsync(SweepResult sweep, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var existingRegistry = _repo.GetAll();
        var preSweepStatuses = existingRegistry.ToDictionary(d => d.Id, d => d.Status ?? "unknown");
        
        var settings = _settingsStore.Load();
        var updatesToCommit = new List<Device>();

        // 1. Map existing for fast lookup
        var ipMap = existingRegistry.GroupBy(d => d.IpAddress).ToDictionary(g => g.Key, g => g.OrderByDescending(d => d.LastSeen).First());
        var macMap = existingRegistry.Where(d => !string.IsNullOrEmpty(d.MacAddress)).GroupBy(d => d.MacAddress!).ToDictionary(g => g.Key, g => g.First());

        var onlineIpsInSweep = new HashSet<string>();

        // 2. Process Online Hosts (per-subnet to preserve discovery interface provenance)
        foreach (var subnet in sweep.Subnets)
        {
            foreach (var host in subnet.Hosts.Where(h => h.IsOnline))
            {
                ct.ThrowIfCancellationRequested();
                onlineIpsInSweep.Add(host.Ip);

                string cleanMac = DeviceMacNormalizer.Normalize(host.Mac);

                Device? device = FindMatchingDevice(cleanMac, host.Ip, subnet.DiscoveryScopeId, macMap, ipMap);

                if (!string.IsNullOrEmpty(cleanMac))
                    _leaseRepo.UpsertLease(cleanMac, host.Ip, host.Hostname, host.Vendor);

                if (device != null)
                {
                    StampDiscoveryProvenance(device, subnet, _logger);
                    UpdateDevice(device, host, now, updatesToCommit);
                }
                else if (!string.IsNullOrEmpty(cleanMac) || HasCorroboratedIdentity(host))
                {
                    if (string.IsNullOrEmpty(cleanMac))
                        _logger.LogDebug("Sync: Admitting new host {Ip} without MAC (ping + enrichment corroboration)", host.Ip);

                    var newDevice = CreateNewDevice(host, now, subnet);
                    updatesToCommit.Add(newDevice);
                    if (!string.IsNullOrEmpty(cleanMac))
                        macMap[cleanMac] = newDevice;
                    ipMap[host.Ip] = newDevice;
                }
                else
                {
                    _logger.LogDebug("Sync: Skipping new host {Ip} because it has no MAC address (Ghost prevention)", host.Ip);
                }
            }
        }

        // 3. Process Offline Transition & Missed Devices
        var currentlyOnline = existingRegistry.Where(d => d.Status == "online").ToList();
        foreach (var device in currentlyOnline)
        {
            if (onlineIpsInSweep.Contains(device.IpAddress)) continue;

            int strikes = _strikeTracker.AddOrUpdate(device.IpAddress, 1, (_, val) => val + 1);
            device.CurrentStrikes = strikes;

            if (strikes >= settings.OfflineStrikeThreshold)
            {
                device.Status = "offline";
                device.LastStateChangeUtc = now;
                device.FlapCount++;
                _strikeTracker.TryRemove(device.IpAddress, out _);
                if (_offlineLoggedIps.TryAdd(device.IpAddress, 0))
                {
                    _logger.LogWarning("Device Offline: {Ip} (Threshold reached)", device.IpAddress);
                }
                else
                {
                    _logger.LogDebug("Device Offline: {Ip} (Threshold reached, suppressed repeat)", device.IpAddress);
                }
                
                // --- RESTORED: Event Logging ---
                _events.AddEvent(new SystemEvent
                {
                    Type = "Network",
                    Message = $"Device Offline: {device.Name} ({device.IpAddress})",
                    Severity = "Warning",
                    DeviceIp = device.IpAddress
                });
                // ------------------------------
                
                updatesToCommit.Add(device);
            }
        }

        // 4. Update Stability for Missed/Offline Devices
        foreach (var device in existingRegistry)
        {
            // If the device was in a scanned subnet but NOT found online, increment seen but not online
            if (!onlineIpsInSweep.Contains(device.IpAddress) && sweep.Subnets.Any(s => NetworkHelper.IsIpInCidr(device.IpAddress, s.Cidr)))
            {
                device.TotalSweepsSeen++;
                DeviceStabilityCalculator.UpdateDeviceStability(device);
                
                // Only commit if we haven't already added it to the update list
                if (!updatesToCommit.Contains(device))
                {
                    updatesToCommit.Add(device);
                }
            }
        }

        // 5. History & Metrics
        _history.Add(new SweepHistoryEntry { Start = sweep.Start, End = sweep.End, TotalOnline = sweep.TotalOnline, TotalHosts = sweep.TotalHosts });

        if (updatesToCommit.Count > 0)
            _repo.UpsertDevices(updatesToCommit);

        return (existingRegistry, preSweepStatuses);
    }

    private static bool HasCorroboratedIdentity(HostScanResult host)
    {
        if (!string.IsNullOrWhiteSpace(host.HttpTitle)) return true;
        if (host.OpenPorts is { Count: > 0 }) return true;
        if (!string.IsNullOrWhiteSpace(host.Hostname)
            && !string.Equals(host.Hostname, host.Ip, StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    private static Device? FindMatchingDevice(
        string cleanMac,
        string ip,
        string? scopeId,
        Dictionary<string, Device> macMap,
        Dictionary<string, Device> ipMap)
    {
        if (!string.IsNullOrEmpty(cleanMac) && macMap.TryGetValue(cleanMac, out var byMac))
        {
            if (ScopesCompatible(byMac.DiscoveryScopeId, scopeId))
                return byMac;
        }

        if (ipMap.TryGetValue(ip, out var byIp))
        {
            if (ScopesCompatible(byIp.DiscoveryScopeId, scopeId))
                return byIp;
        }

        // Scope IDs can change after settings migrations (e.g. VLAN tagging). Prefer identity over scope.
        if (!string.IsNullOrEmpty(cleanMac) && macMap.TryGetValue(cleanMac, out byMac))
            return byMac;

        if (ipMap.TryGetValue(ip, out byIp))
            return byIp;

        return null;
    }

    private static bool ScopesCompatible(string? existingScopeId, string? incomingScopeId)
    {
        if (string.IsNullOrWhiteSpace(existingScopeId) || string.IsNullOrWhiteSpace(incomingScopeId))
            return true;

        return string.Equals(existingScopeId, incomingScopeId, StringComparison.OrdinalIgnoreCase);
    }

    private static void StampDiscoveryProvenance(Device device, SubnetScanResult subnet, ILogger logger)
    {
        if (device.IsDiscoveryProvenanceManuallySet)
            return;

        if (string.IsNullOrWhiteSpace(device.DiscoveryScopeId) && !string.IsNullOrWhiteSpace(subnet.DiscoveryScopeId))
            device.DiscoveryScopeId = subnet.DiscoveryScopeId;

        if (string.IsNullOrWhiteSpace(device.DiscoveryInterfaceId) && !string.IsNullOrWhiteSpace(subnet.DiscoveryInterfaceId))
            device.DiscoveryInterfaceId = subnet.DiscoveryInterfaceId;

        if (string.IsNullOrWhiteSpace(device.DiscoveryInterfaceName) && !string.IsNullOrWhiteSpace(subnet.DiscoveryInterfaceName))
            device.DiscoveryInterfaceName = subnet.DiscoveryInterfaceName;

        if (string.IsNullOrWhiteSpace(device.DiscoveryVlanTag) && !string.IsNullOrWhiteSpace(subnet.DiscoveryVlanTag))
        {
            device.DiscoveryVlanTag = subnet.DiscoveryVlanTag;
            logger.LogDebug(
                "Discovery provenance: Backfilled VLAN tag {VlanTag} for device {DeviceLabel} ({Ip}) from scope {ScopeId}",
                subnet.DiscoveryVlanTag,
                FormatDeviceLabel(device),
                device.IpAddress,
                subnet.DiscoveryScopeId ?? subnet.Cidr);
        }
    }

    private static string FormatDeviceLabel(Device device)
    {
        if (!string.IsNullOrWhiteSpace(device.Name) && !string.Equals(device.Name, device.IpAddress, StringComparison.OrdinalIgnoreCase))
            return device.Name;
        if (!string.IsNullOrWhiteSpace(device.Hostname))
            return device.Hostname;
        if (!string.IsNullOrWhiteSpace(device.MacAddress))
            return device.MacAddress;
        return device.IpAddress;
    }

    private void UpdateDevice(Device d, HostScanResult h, DateTime now, List<Device> updates)
    {
        var originalStatus = d.Status;
        var originalIp = d.IpAddress;

        var fedSettings = _fedSettingsStore.Current;
        bool hierarchyChanged = false;
        if (string.IsNullOrWhiteSpace(d.NodeId) || d.NodeId != fedSettings.NodeId)
        {
            d.NodeId = fedSettings.NodeId;
            hierarchyChanged = true;
        }
        if (d.Client != fedSettings.Client)
        {
            d.Client = fedSettings.Client;
            hierarchyChanged = true;
        }
        if (d.Building != fedSettings.Building)
        {
            d.Building = fedSettings.Building;
            hierarchyChanged = true;
        }
        if (d.Room != fedSettings.Room)
        {
            d.Room = fedSettings.Room;
            hierarchyChanged = true;
        }

        d.LastSeen = now;
        d.IpAddress = h.Ip;
        var sweepMac = DeviceMacNormalizer.Normalize(h.Mac);
        if (!string.IsNullOrEmpty(sweepMac))
            d.MacAddress = sweepMac;
        d.LastLatencyMs = h.RoundtripTimeMs;
        if (h.Vendor != "Unknown Vendor") d.Vendor = h.Vendor;
        d.OpenPorts = h.OpenPorts ?? d.OpenPorts;

        // --- NEW: Telemetry & Security Updates ---
        d.TotalSweepsSeen++;
        d.TotalSweepsOnline++;
        
        // Add to Latency History (Max 50)
        d.LatencyHistory ??= new List<long>();
        d.LatencyHistory.Add(h.RoundtripTimeMs);
        if (d.LatencyHistory.Count > 50) d.LatencyHistory.RemoveAt(0);

        // Recalculate Stability
        DeviceStabilityCalculator.UpdateDeviceStability(d);

        // Recalculate Security Audit
        var priorGrade = d.SecurityGrade;
        var priorIssuesKey = string.Join('\u001f', d.SecurityIssues ?? []);
        var audit = _securityAuditor.AuditDevice(d);
        d.SecurityScore = audit.Score;
        d.SecurityGrade = audit.Grade;
        d.SecurityIssues = audit.Issues;
        var securityChanged = d.SecurityGrade != priorGrade
            || string.Join('\u001f', d.SecurityIssues) != priorIssuesKey;
        // ------------------------------------------

        // Roaming detection
        if (originalIp != d.IpAddress)
        {
            _logger.LogInformation("Device Roamed: {Mac} to {Ip}", d.MacAddress, d.IpAddress);
            d.Status = "online";
            d.CurrentStrikes = 0;
            
            // --- RESTORED: Event Logging ---
            _events.AddEvent(new SystemEvent
            {
                Type = "Network",
                Message = $"Device Roamed: {d.Name} moved to {d.IpAddress}",
                Severity = "Info",
                DeviceIp = d.IpAddress
            });
            // ------------------------------
        }

        // Recovery logic
        if (d.Status == "offline")
        {
            int rec = _recoveryTracker.AddOrUpdate(h.Ip, 1, (_, v) => v + 1);
            if (rec >= RECOVERY_THRESHOLD || _settingsStore.Load().EnableInstantRecovery)
            {
                d.Status = "online";
                d.CurrentStrikes = 0;
                d.LastStateChangeUtc = now;
                _recoveryTracker.TryRemove(h.Ip, out _);
                _offlineLoggedIps.TryRemove(h.Ip, out _);

                // --- RESTORED: Event Logging ---
                _events.AddEvent(new SystemEvent
                {
                    Type = "Network",
                    Message = $"Device Recovery: {d.Name} ({d.IpAddress}) is back online",
                    Severity = "Info",
                    DeviceIp = d.IpAddress
                });
                // ------------------------------
            }
        }

        _strikeTracker.TryRemove(h.Ip, out _);
        
        if (originalStatus != d.Status || hierarchyChanged || securityChanged || (now - d.LastDbWriteUtc) > WriteThrottle)
        {
            d.LastDbWriteUtc = now;
            if (!updates.Contains(d)) updates.Add(d);
        }
    }

    private Device CreateNewDevice(HostScanResult h, DateTime now, SubnetScanResult subnet)
    {
        var fedSettings = _fedSettingsStore.Current;
        var d = new Device
        {
            IpAddress = h.Ip,
            Hostname = h.Hostname,
            Name = string.IsNullOrWhiteSpace(h.Hostname) ? h.Ip : h.Hostname.Trim(),
            Status = "online",
            FirstSeen = now,
            LastSeen = now,
            LastStateChangeUtc = now,
            MacAddress = DeviceMacNormalizer.Normalize(h.Mac),
            Vendor = h.Vendor,
            OpenPorts = h.OpenPorts ?? [],
            TotalSweepsSeen = 1,
            TotalSweepsOnline = 1,
            LastLatencyMs = h.RoundtripTimeMs,
            LatencyHistory = new List<long> { h.RoundtripTimeMs },
            NodeId = fedSettings.NodeId,
            Client = fedSettings.Client,
            Building = fedSettings.Building,
            Room = fedSettings.Room,
            DiscoveryScopeId = subnet.DiscoveryScopeId,
            DiscoveryInterfaceId = subnet.DiscoveryInterfaceId,
            DiscoveryInterfaceName = subnet.DiscoveryInterfaceName,
            DiscoveryVlanTag = subnet.DiscoveryVlanTag
        };

        // Initial stability calculation
        DeviceStabilityCalculator.UpdateDeviceStability(d);

        // Initial security audit
        var audit = _securityAuditor.AuditDevice(d);
        d.SecurityScore = audit.Score;
        d.SecurityGrade = audit.Grade;
        d.SecurityIssues = audit.Issues;

        return d;
    }
}
