using LiteDB;
using Microsoft.Extensions.Logging;
using StacksAtlas.Core.Abstractions;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Services.Devices;

namespace StacksAtlas.Core.Data;

public interface IDeviceRepository
{
    Device? GetById(Guid id);
    Device? GetByMac(string mac);
    Device? GetByIp(string ip);
    void UpsertDevice(Device incoming, bool isUserAction = false);
    void UpsertDevices(IEnumerable<Device> devices, bool isUserAction = false);
    List<Device> GetAll(bool includeDeleted = false, bool includePermanentlyRemoved = false);
    
    List<Device> GetPaged(
        int skip, 
        int take, 
        string? search, 
        string? status, 
        bool showArchived, 
        string? nodeId,
        string? client,
        string? building,
        string? room,
        string[]? vlanTags,
        out int totalCount,
        string? attachmentKind = null,
        string? attachmentPort = null,
        string? attachmentParentId = null);

    List<string> GetDistinctDiscoveryVlanTags(string? nodeId = null);

    List<Device> GetAttachmentParentCandidates(string? nodeId, Guid? excludeDeviceId = null, int take = 2000);

    int GetCount();
    bool Delete(Guid id);
    bool Restore(Guid id);
    bool HardDelete(Guid id);
    bool RemoveFromFleet(Guid id, string? removedBy = null, string? reason = null);
    bool RestoreFromFleet(Guid id);
    List<Device> GetPermanentlyRemoved();
    List<Device> GetDeleted();
    List<DeviceRepository.DeviceSummaryItem> GetSummaryData();
    bool AcknowledgeSecurityIssue(Guid id, string issue);
    bool ResetMetrics(Guid id);
}

public class DeviceRepository : IDeviceRepository
{
    private readonly LiteDatabase _db;
    private readonly ILogger<DeviceRepository> _logger;
    private readonly DeviceReconciliationService _reconciliationService;
    private readonly DeviceTombstoneGate _tombstoneGate;
    private readonly IClock _clock;
    private readonly StacksAtlas.Core.Settings.FederationSettingsStore? _settingsStore;
    private readonly IEventRepository? _eventRepository;
    
    // Memory Cache for Count (Throttles redundant UI polls)
    private int? _cachedCount;
    private DateTime _countCacheExpiration = DateTime.MinValue;
    private readonly Lock _countLock = new();

    private ILiteCollection<Device> Collection =>
        _db.GetCollection<Device>("devices");

    public DeviceRepository(LiteDatabase db, ILogger<DeviceRepository> logger, DeviceReconciliationService reconciliationService, DeviceTombstoneGate tombstoneGate, IClock clock, StacksAtlas.Core.Settings.FederationSettingsStore? settingsStore = null, IEventRepository? eventRepository = null)
    {
        _db = db;
        _logger = logger;
        _reconciliationService = reconciliationService;
        _tombstoneGate = tombstoneGate;
        _clock = clock;
        _settingsStore = settingsStore;
        _eventRepository = eventRepository;
        
        // Ensure critical indexes are present for hyper-performance
        var col = Collection;
        col.EnsureIndex(x => x.IpAddress);
        col.EnsureIndex(x => x.MacAddress);
        col.EnsureIndex(x => x.IsDeleted);
        col.EnsureIndex(x => x.ArchivedUtc);
        col.EnsureIndex(x => x.IsPermanentlyRemoved);
        col.EnsureIndex(x => x.FirstSeen); // Used for default sorting
    }

    public Device? GetById(Guid id)
    {
        var device = Collection.FindById(id);
        if (device != null) PopulateLocationDetails(new[] { device });
        return device;
    }

    private static string NormalizeMac(string? mac)
    {
        if (string.IsNullOrWhiteSpace(mac)) return string.Empty;
        // Standard: Uppercase, no separators (e.g., 18C04D885B57)
        return mac.Replace("-", "").Replace(":", "").Replace(".", "").Replace(" ", "").ToUpperInvariant();
    }

    private void RemapAttachmentParent(Device device)
    {
        var parentMac = DeviceMacNormalizer.Normalize(device.AttachmentParentMac);
        device.AttachmentParentMac = string.IsNullOrEmpty(parentMac) ? null : parentMac;
        var parentIp = string.IsNullOrWhiteSpace(device.AttachmentParentIp)
            ? null
            : device.AttachmentParentIp.Trim();
        device.AttachmentParentIp = parentIp;

        static bool SameSite(Device child, Device parent) =>
            string.IsNullOrEmpty(child.NodeId)
            || string.IsNullOrEmpty(parent.NodeId)
            || string.Equals(child.NodeId, parent.NodeId, StringComparison.OrdinalIgnoreCase);

        if (device.AttachmentParentDeviceId is Guid pid)
        {
            var byId = GetById(pid);
            if (byId != null && byId.Id != device.Id && SameSite(device, byId))
            {
                device.AttachmentParentDeviceId = byId.Id;
                var mac = DeviceMacNormalizer.Normalize(byId.MacAddress);
                device.AttachmentParentMac = string.IsNullOrEmpty(mac) ? null : mac;
                device.AttachmentParentIp = string.IsNullOrWhiteSpace(byId.IpAddress) ? parentIp : byId.IpAddress.Trim();
                return;
            }
        }

        Device? parent = null;
        if (!string.IsNullOrEmpty(parentMac))
            parent = ResolveParentByMac(parentMac, device.NodeId);
        if (parent == null && !string.IsNullOrEmpty(parentIp))
            parent = ResolveParentByIp(parentIp, device.NodeId);

        if (parent != null && parent.Id != device.Id)
        {
            device.AttachmentParentDeviceId = parent.Id;
            var mac = DeviceMacNormalizer.Normalize(parent.MacAddress);
            device.AttachmentParentMac = string.IsNullOrEmpty(mac) ? null : mac;
            device.AttachmentParentIp = string.IsNullOrWhiteSpace(parent.IpAddress) ? parentIp : parent.IpAddress.Trim();
            return;
        }

        if (device.AttachmentParentDeviceId is Guid stalePid)
        {
            var local = GetById(stalePid);
            if (local == null || local.Id == device.Id)
            {
                if (!device.IsAttachmentManuallySet)
                    device.AttachmentParentDeviceId = null;
                return;
            }

            var mac = DeviceMacNormalizer.Normalize(local.MacAddress);
            device.AttachmentParentMac = string.IsNullOrEmpty(mac) ? null : mac;
            device.AttachmentParentIp = string.IsNullOrWhiteSpace(local.IpAddress) ? parentIp : local.IpAddress.Trim();
        }
    }

    private Device? ResolveParentByMac(string mac, string? nodeId)
    {
        var normalized = DeviceMacNormalizer.Normalize(mac);
        if (string.IsNullOrEmpty(normalized))
            return null;

        var matches = Collection.Find(d =>
            d.MacAddress == normalized && d.IsDeleted != true && d.IsPermanentlyRemoved != true);
        if (!string.IsNullOrWhiteSpace(nodeId))
            matches = matches.Where(d => string.Equals(d.NodeId, nodeId, StringComparison.OrdinalIgnoreCase));

        return matches.FirstOrDefault();
    }

    private Device? ResolveParentByIp(string ip, string? nodeId)
    {
        if (string.IsNullOrWhiteSpace(ip))
            return null;

        var matches = Collection.Find(d =>
            d.IpAddress == ip && d.IsDeleted != true && d.IsPermanentlyRemoved != true);
        if (!string.IsNullOrWhiteSpace(nodeId))
            matches = matches.Where(d => string.Equals(d.NodeId, nodeId, StringComparison.OrdinalIgnoreCase));

        return matches.FirstOrDefault();
    }

    public Device? GetByMac(string mac)
    {
        if (string.IsNullOrWhiteSpace(mac)) return null;

        var norm = NormalizeMac(mac); 

        // Look for the normalized version. We also check the lowercase version for legacy data compatibility.
        var device = Collection.FindOne(x => x.MacAddress == norm || x.MacAddress == mac.ToLowerInvariant());
        if (device != null) PopulateLocationDetails(new[] { device });
        return device;
    }

    public Device? GetByIp(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return null;
        var device = Collection.FindOne(d => d.IpAddress == ip.Trim());
        if (device != null) PopulateLocationDetails(new[] { device });
        return device;
    }

    public void UpsertDevice(Device incoming, bool isUserAction = false)
    {
        try
        {
            var col = Collection;

            // --- MAC NORMALIZATION ---
            string normalizedMac = !string.IsNullOrWhiteSpace(incoming.MacAddress)
                ? incoming.MacAddress.Replace("-", "").Replace(":", "").Replace(".", "").ToUpperInvariant()
                : string.Empty;

            incoming.MacAddress = normalizedMac;
            if (string.IsNullOrWhiteSpace(incoming.NodeId) && _settingsStore != null)
                incoming.NodeId = _settingsStore.Current.NodeId ?? string.Empty;

            // --- 1. FIND EXISTING DEVICE ---
            Device? existing = null;
            if (!string.IsNullOrWhiteSpace(normalizedMac))
                existing = col.FindOne(x => x.MacAddress == normalizedMac);

            existing ??= col.FindOne(x => x.IpAddress == incoming.IpAddress);

            if (_tombstoneGate.ShouldBlockDiscoveryUpsert(existing, incoming, isUserAction, out var tombstone))
            {
                tombstone ??= FindTombstone(col, _tombstoneGate, incoming, normalizedMac);
                if (tombstone != null)
                {
                    _tombstoneGate.ApplyRediscoveryHit(tombstone, incoming.IpAddress);
                    col.Update(tombstone);
                }
                return;
            }

            if (existing != null)
            {
                if (existing.IsDeleted && !isUserAction)
                    return;

                var reconciled = _reconciliationService.ReconcileExisting(existing, incoming, isUserAction);
                RemapAttachmentParent(reconciled);
                reconciled.LastModifiedUtc = _clock.UtcNow;
                col.Update(reconciled);
            }
            else
            {
                // --- NEW DEVICE ---
                var newDevice = _reconciliationService.PrepareNewDevice(incoming);
                RemapAttachmentParent(newDevice);
                newDevice.LastModifiedUtc = _clock.UtcNow;
                col.Insert(newDevice);
                LogNewDeviceDiscovered(newDevice);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Upsert failed for IP: {Ip}, MAC: {Mac}", incoming.IpAddress, incoming.MacAddress);
        }
    }

    public void UpsertDevices(IEnumerable<Device> devices, bool isUserAction = false)
    {
        // PERFORMANCE: Large updates ( sweeps) can lock the DB for seconds.
        // We split into batches and yield to allow Web API reads to "sneak in".
        const int BatchSize = 25;
        var deviceList = devices.ToList();

        for (int i = 0; i < deviceList.Count; i += BatchSize)
        {
            var batch = deviceList.Skip(i).Take(BatchSize);
            
            try
            {
                _db.BeginTrans();
                foreach (var device in batch)
                {
                    UpsertDevice(device, isUserAction);
                }
                _db.Commit();
                
                // FORCE YIELD: Release the lock for a fraction of a second
                // to allow the Web Server threads to fulfill UI requests.
                if (deviceList.Count > BatchSize)
                {
                    Thread.Sleep(5); // 5ms gap is enough for a read query to finish
                }
            }
            catch (Exception ex)
            {
                _db.Rollback();
                _logger.LogError(ex, "Batch Upsert failed at index {Index}.", i);
            }
        }
        
        // Invalidate count cache after batch update
        lock (_countLock)
        {
            _cachedCount = null;
        }
    }
    public List<Device> GetAll(bool includeDeleted = false, bool includePermanentlyRemoved = false)
    {
        var list = Collection.Find(x =>
            (includePermanentlyRemoved || x.IsPermanentlyRemoved != true) &&
            (includeDeleted || x.IsDeleted != true)).ToList();
        PopulateLocationDetails(list);
        return list;
    }

    public List<Device> GetPaged(
        int skip,
        int take,
        string? search,
        string? status,
        bool showArchived,
        string? nodeId,
        string? client,
        string? building,
        string? room,
        string[]? vlanTags,
        out int totalCount,
        string? attachmentKind = null,
        string? attachmentPort = null,
        string? attachmentParentId = null)
    {
        var localSettings = _settingsStore?.Current;

        // If client, building, or room filters are specified, validate them against local settings
        if (localSettings != null)
        {
            if (!string.IsNullOrWhiteSpace(client) && !client.Equals(localSettings.Client, StringComparison.OrdinalIgnoreCase))
            {
                totalCount = 0;
                return new List<Device>();
            }
            if (!string.IsNullOrWhiteSpace(building) && !building.Equals(localSettings.Building, StringComparison.OrdinalIgnoreCase))
            {
                totalCount = 0;
                return new List<Device>();
            }
            if (!string.IsNullOrWhiteSpace(room) && !room.Equals(localSettings.Room, StringComparison.OrdinalIgnoreCase))
            {
                totalCount = 0;
                return new List<Device>();
            }
        }

        // Base query - in Standalone/Node mode, we ignore database columns for location filters as they are empty in DB
        var query = Collection.Query()
            .Where(x => !x.IsPermanentlyRemoved)
            .Where(x => showArchived ? x.IsDeleted == true : x.IsDeleted != true);

        if (!string.IsNullOrWhiteSpace(status) && !status.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            if (status.Equals("New", StringComparison.OrdinalIgnoreCase))
            {
                var cutoff = DateTime.UtcNow.AddHours(-24);
                query = query.Where(d => d.FirstSeen >= cutoff);
            }
            else
            {
                var s = status.ToLower();
                query = query.Where(d => d.Status.ToLower() == s);
            }
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.ToLower().Trim();
            query = query.Where(d => 
                (d.Name != null && d.Name.ToLower().Contains(s)) || 
                (d.IpAddress != null && d.IpAddress.Contains(s)) || 
                (d.MacAddress != null && d.MacAddress.ToLower().Contains(s)) ||
                (d.Vendor != null && d.Vendor.ToLower().Contains(s)) ||
                (d.AttachmentPort != null && d.AttachmentPort.ToLower().Contains(s))
            );
        }

        if (!string.IsNullOrWhiteSpace(nodeId))
        {
            query = query.Where(d => d.NodeId == nodeId);
        }

        var list = query.ToList();
        list = ApplyVlanTagFilter(list, vlanTags);
        list = ApplyAttachmentFilters(list, attachmentKind, attachmentPort, attachmentParentId);

        totalCount = list.Count;

        list = list
            .OrderBy(d => d.FirstSeen)
            .Skip(skip)
            .Take(take)
            .ToList();

        PopulateLocationDetails(list);
        return list;
    }

    public List<string> GetDistinctDiscoveryVlanTags(string? nodeId = null)
    {
        var query = Collection.Find(x => x.IsDeleted != true && x.IsPermanentlyRemoved != true);
        if (!string.IsNullOrWhiteSpace(nodeId))
            query = query.Where(d => d.NodeId == nodeId);

        return query
            .Select(d => d.DiscoveryVlanTag)
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public List<Device> GetAttachmentParentCandidates(string? nodeId, Guid? excludeDeviceId = null, int take = 2000)
    {
        var query = Collection.Find(x => x.IsDeleted != true && x.IsPermanentlyRemoved != true);
        if (!string.IsNullOrWhiteSpace(nodeId))
            query = query.Where(d => d.NodeId == nodeId);

        var list = DeviceAttachmentParentCatalog
            .FilterCandidates(query, excludeDeviceId)
            .Take(Math.Clamp(take, 1, 5000))
            .ToList();

        PopulateLocationDetails(list);
        return list;
    }

    public static List<Device> ApplyAttachmentFilters(
        List<Device> devices,
        string? attachmentKind,
        string? attachmentPort,
        string? attachmentParentId)
    {
        var list = devices;

        if (!string.IsNullOrWhiteSpace(attachmentKind)
            && !attachmentKind.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            var kind = attachmentKind.Trim();
            list = list.Where(d =>
                string.Equals(d.AttachmentKind ?? "Unknown", kind, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (!string.IsNullOrWhiteSpace(attachmentPort))
        {
            var portNeedle = attachmentPort.Trim();
            list = list.Where(d =>
                !string.IsNullOrEmpty(d.AttachmentPort)
                && d.AttachmentPort.Contains(portNeedle, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (!string.IsNullOrWhiteSpace(attachmentParentId))
        {
            if (attachmentParentId.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                list = list.Where(d => d.AttachmentParentDeviceId == null).ToList();
            }
            else if (attachmentParentId.Equals("any", StringComparison.OrdinalIgnoreCase))
            {
                list = list.Where(d => d.AttachmentParentDeviceId != null).ToList();
            }
            else if (Guid.TryParse(attachmentParentId, out var parentId))
            {
                list = list.Where(d => d.AttachmentParentDeviceId == parentId).ToList();
            }
        }

        return list;
    }

    private static List<Device> ApplyVlanTagFilter(List<Device> devices, string[]? vlanTags)
    {
        if (vlanTags == null || vlanTags.Length == 0)
            return devices;

        var includeUntagged = vlanTags.Contains("__untagged__", StringComparer.OrdinalIgnoreCase);
        var explicitTags = vlanTags
            .Where(tag => !tag.Equals("__untagged__", StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return devices.Where(d =>
        {
            var hasTag = !string.IsNullOrWhiteSpace(d.DiscoveryVlanTag);
            if (!hasTag)
                return includeUntagged;

            return explicitTags.Contains(d.DiscoveryVlanTag!);
        }).ToList();
    }

    public int GetCount()
    {
        lock (_countLock)
        {
            if (_cachedCount.HasValue && _clock.UtcNow < _countCacheExpiration)
            {
                return _cachedCount.Value;
            }

            _cachedCount = Collection.Count(x => x.IsDeleted != true && x.IsPermanentlyRemoved != true);
            _countCacheExpiration = _clock.UtcNow.AddSeconds(1); // 1.0s TTL
            return _cachedCount.Value;
        }
    }

    // 2. Add the Soft Delete method
    public bool Delete(Guid id) // We name it Delete so the Controller is happy
    {
        try
        {
            var device = GetById(id);
            if (device == null) return false;

            device.IsDeleted = true; // Just flag it, don't drop the row
            device.ArchivedUtc ??= _clock.UtcNow;
            device.Status = "offline";
            device.LastModifiedUtc = _clock.UtcNow;
            return Collection.Update(device);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Soft Delete failed for ID: {Id}", id);
            return false;
        }
    }

    // 3. Add the Restore method for the Undo button
    public bool Restore(Guid id)
    {
        var device = GetById(id);
        if (device == null || device.IsPermanentlyRemoved) return false;

        device.IsDeleted = false;
        device.ArchivedUtc = null;
        device.LastModifiedUtc = _clock.UtcNow;
        return Collection.Update(device);
    }

    public bool RemoveFromFleet(Guid id, string? removedBy = null, string? reason = null)
    {
        var device = GetById(id);
        if (device == null) return false;

        device.IsPermanentlyRemoved = true;
        device.IsDeleted = true;
        device.ArchivedUtc ??= _clock.UtcNow;
        device.RemovedUtc = _clock.UtcNow;
        device.RemovedBy = removedBy;
        device.RemovedReason = reason;
        device.LastModifiedUtc = _clock.UtcNow;

        if (!Collection.Update(device)) return false;

        _tombstoneGate.RegisterSuppression(device, removedBy);
        lock (_countLock) { _cachedCount = null; }
        return true;
    }

    public bool RestoreFromFleet(Guid id)
    {
        var device = GetById(id);
        if (device == null || !device.IsPermanentlyRemoved) return false;

        device.IsPermanentlyRemoved = false;
        device.IsDeleted = false;
        device.ArchivedUtc = null;
        device.RemovedUtc = null;
        device.RemovedBy = null;
        device.RemovedReason = null;
        device.LastModifiedUtc = _clock.UtcNow;

        if (!Collection.Update(device)) return false;

        _tombstoneGate.ClearSuppression(device);
        lock (_countLock) { _cachedCount = null; }
        return true;
    }

    public List<Device> GetPermanentlyRemoved()
    {
        var list = Collection.Find(x => x.IsPermanentlyRemoved == true).ToList();
        PopulateLocationDetails(list);
        EnrichRemovedByFromSuppressions(list);
        return list;
    }

    // 4. Hard Delete (Nuke it  -  internal/admin only; fleet removal uses RemoveFromFleet)
    public bool HardDelete(Guid id)
    {
        var device = GetById(id);
        if (device != null && device.IsPermanentlyRemoved)
            _tombstoneGate.ClearSuppression(device);
        return Collection.Delete(id);
    }

    // Returns archived items only (not permanently removed)
    public List<Device> GetDeleted() =>
        Collection.Find(x => x.IsDeleted == true && x.IsPermanentlyRemoved != true).ToList();

    public class DeviceSummaryItem
    {
        public string? Status { get; set; }
        public string? Vendor { get; set; }
        public string? Type { get; set; }
    }

    public List<DeviceSummaryItem> GetSummaryData()
    {
        return Collection.Query()
            .Where(x => x.IsDeleted != true && x.IsPermanentlyRemoved != true)
            .Select(x => new { x.Status, x.Vendor, x.Type })
            .ToEnumerable()
            .Select(x => new DeviceSummaryItem 
            { 
                Status = x.Status, 
                Vendor = x.Vendor, 
                Type = x.Type 
            })
            .ToList();
    }
    public bool AcknowledgeSecurityIssue(Guid id, string issue)
    {
        var device = GetById(id);
        if (device == null) return false;

        if (device.IgnoredSecurityIssues == null) device.IgnoredSecurityIssues = [];
        
        if (!device.IgnoredSecurityIssues.Contains(issue))
        {
            device.IgnoredSecurityIssues.Add(issue);
            device.LastModifiedUtc = _clock.UtcNow;
            return Collection.Update(device);
        }
        return true; // Already ignored
    }

    public bool ResetMetrics(Guid id)
    {
        var device = GetById(id);
        if (device == null) return false;

        // Reset all performance counters
        device.FlapCount = 0;
        device.LatencyHistory = []; 
        device.AverageLatencyMs = 0;
        device.StabilityScore = 100; // Reset to perfect health
        device.CurrentStrikes = 0;
        
        // Optional: Reset uptime tracker? No, that's historical. 
        // We just want to clear the "bad" signals.

        device.LastModifiedUtc = _clock.UtcNow;
        return Collection.Update(device);
    }

    private void EnrichRemovedByFromSuppressions(List<Device> devices)
    {
        if (devices.Count == 0) return;

        var suppressions = _db.GetCollection<DeviceSuppression>("device_suppressions").FindAll().ToList();
        if (suppressions.Count == 0) return;

        foreach (var device in devices)
        {
            if (!string.IsNullOrWhiteSpace(device.RemovedBy))
                continue;

            var mac = DeviceMacNormalizer.Normalize(device.MacAddress);
            var match = suppressions.FirstOrDefault(s => s.DeviceId == device.Id)
                ?? suppressions.FirstOrDefault(s =>
                    s.NodeId == (_tombstoneGate.ResolveNodeId(device)) &&
                    s.NormalizedMac == mac);

            if (!string.IsNullOrWhiteSpace(match?.RemovedBy))
                device.RemovedBy = match.RemovedBy;
        }
    }

    private static Device? FindTombstone(ILiteCollection<Device> col, DeviceTombstoneGate gate, Device incoming, string normalizedMac)
    {
        if (string.IsNullOrEmpty(normalizedMac)) return null;
        var nodeId = gate.ResolveNodeId(incoming);
        return col.FindOne(x => x.IsPermanentlyRemoved && x.NodeId == nodeId && x.MacAddress == normalizedMac)
            ?? col.FindOne(x => x.IsPermanentlyRemoved && x.MacAddress == normalizedMac);
    }

    private void LogNewDeviceDiscovered(Device device)
    {
        var label = !string.IsNullOrWhiteSpace(device.Name) && device.Name != device.IpAddress
            ? device.Name
            : !string.IsNullOrWhiteSpace(device.Hostname)
                ? device.Hostname
                : !string.IsNullOrWhiteSpace(device.MacAddress)
                    ? device.MacAddress
                    : device.IpAddress;

        if (!string.IsNullOrWhiteSpace(device.DiscoveryVlanTag))
        {
            _logger.LogInformation(
                "Discovery: New device {DeviceLabel} ({Ip}) on {InterfaceName} [VLAN {VlanTag}]",
                label,
                device.IpAddress,
                device.DiscoveryInterfaceName ?? "unknown interface",
                device.DiscoveryVlanTag);
        }
        else
        {
            _logger.LogInformation(
                "Discovery: New device {DeviceLabel} ({Ip})",
                label,
                device.IpAddress);
        }

        RecordDiscoveryEvent(device, label);
    }

    private void RecordDiscoveryEvent(Device device, string label)
    {
        if (_eventRepository == null)
            return;

        var vlanSuffix = string.IsNullOrWhiteSpace(device.DiscoveryVlanTag) ? string.Empty : $" [VLAN: {device.DiscoveryVlanTag}]";
        var ifaceSuffix = string.IsNullOrWhiteSpace(device.DiscoveryInterfaceName)
            ? string.Empty
            : $" via {device.DiscoveryInterfaceName}";

        _eventRepository.AddEvent(new SystemEvent
        {
            Type = "Discovery",
            Message = $"New Device Discovered: {label} ({device.IpAddress}){ifaceSuffix}{vlanSuffix}",
            Severity = "Info",
            DeviceIp = device.IpAddress
        });
    }

    private void PopulateLocationDetails(IEnumerable<Device> devices)
    {
        if (_settingsStore == null) return;
        var settings = _settingsStore.Current;
        foreach (var d in devices)
        {
            d.Client ??= settings.Client;
            d.Building ??= settings.Building;
            d.Room ??= settings.Room;
            if (string.IsNullOrEmpty(d.NodeId))
            {
                d.NodeId = settings.NodeId;
            }
        }
    }
}
