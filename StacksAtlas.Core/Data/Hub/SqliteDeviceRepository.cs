using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StacksAtlas.Core.Abstractions;
using StacksAtlas.Core.Data;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Services.Devices;

namespace StacksAtlas.Core.Data.Hub;

public class SqliteDeviceRepository : IDeviceRepository
{
    private readonly IDbContextFactory<HubDbContext> _contextFactory;
    private readonly ILogger<SqliteDeviceRepository> _logger;
    private readonly DeviceReconciliationService _reconciliationService;
    private readonly DeviceTombstoneGate _tombstoneGate;
    private readonly IDeviceSuppressionRegistry _suppressions;
    private readonly IClock _clock;

    public SqliteDeviceRepository(
        IDbContextFactory<HubDbContext> contextFactory, 
        ILogger<SqliteDeviceRepository> logger, 
        DeviceReconciliationService reconciliationService,
        DeviceTombstoneGate tombstoneGate,
        IDeviceSuppressionRegistry suppressions,
        IClock clock)
    {
        _contextFactory = contextFactory;
        _logger = logger;
        _reconciliationService = reconciliationService;
        _tombstoneGate = tombstoneGate;
        _suppressions = suppressions;
        _clock = clock;
    }

    public Device? GetById(Guid id) 
    {
        using var context = _contextFactory.CreateDbContext();
        return context.Devices.AsNoTracking().FirstOrDefault(d => d.Id == id);
    }

    public Device? GetByMac(string mac)
    {
        if (string.IsNullOrWhiteSpace(mac)) return null;
        var norm = mac.Replace("-", "").Replace(":", "").Replace(".", "").ToUpperInvariant();
        using var context = _contextFactory.CreateDbContext();
        return context.Devices.AsNoTracking().FirstOrDefault(d => d.MacAddress == norm);
    }

    public Device? GetByIp(string ip) 
    {
        using var context = _contextFactory.CreateDbContext();
        return context.Devices.AsNoTracking().FirstOrDefault(d => d.IpAddress == ip);
    }

    public void UpsertDevice(Device incoming, bool isUserAction = false)
    {
        try
        {
            using var context = _contextFactory.CreateDbContext();
            UpsertInternal(context, incoming, isUserAction);
            context.SaveChanges();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SQLite Upsert failed for IP: {Ip}, MAC: {Mac} (Node: {NodeId})", 
                incoming.IpAddress, incoming.MacAddress, incoming.NodeId);
        }
    }

    private static bool IsFederatedLifecycleUpdate(Device existing, Device incoming) =>
        FederationDeviceIdentity.SameFleetDevice(existing, incoming)
        && FederationDeviceIdentity.HasLifecycleDelta(existing, incoming);

    private void UpsertInternal(HubDbContext context, Device incoming, bool isUserAction)
    {
        // --- MAC NORMALIZATION ---
        if (!string.IsNullOrWhiteSpace(incoming.MacAddress))
        {
            incoming.MacAddress = incoming.MacAddress.Replace("-", "").Replace(":", "").Replace(".", "").ToUpperInvariant();
        }

        // --- FLEET LIFECYCLE GOVERNANCE (Node → Hub) ---
        if (!isUserAction
            && !string.IsNullOrWhiteSpace(incoming.NodeId)
            && !string.IsNullOrWhiteSpace(incoming.MacAddress)
            && TryApplyFederatedLifecycleToFleet(context, incoming))
        {
            return;
        }

        // --- FIND EXISTING DEVICE ---
        Device? existing;
        if (!isUserAction)
        {
            existing = FindExistingByNodeAndMac(context, incoming);
            existing ??= FindExistingByNodeAndIp(context, incoming);
            existing ??= context.Devices.Find(incoming.Id);
        }
        else
        {
            existing = context.Devices.Find(incoming.Id);
            existing ??= FindExistingByNodeAndMac(context, incoming);
            existing ??= FindExistingByNodeAndIp(context, incoming);
        }

        if (_tombstoneGate.ShouldBlockDiscoveryUpsert(existing, incoming, isUserAction, out var tombstone))
        {
            tombstone ??= FindTombstone(context, incoming);
            if (tombstone != null)
            {
                _tombstoneGate.ApplyRediscoveryHit(tombstone, incoming.IpAddress);
                context.Devices.Update(tombstone);
            }
            return;
        }

        if (existing != null)
        {
            var lifecycleChanged = IsFederatedLifecycleUpdate(existing, incoming);

            if (existing.IsDeleted && !isUserAction && !lifecycleChanged)
                return;

            var wasPermanentlyRemoved = existing.IsPermanentlyRemoved;

            _reconciliationService.ReconcileExisting(existing, incoming, isUserAction);
            RemapAttachmentParent(context, existing);
            existing.LastModifiedUtc = _clock.UtcNow;

            if (!isUserAction)
            {
                var removedBy = existing.RemovedBy ?? incoming.RemovedBy;
                if (existing.IsPermanentlyRemoved && !wasPermanentlyRemoved)
                    RegisterSuppression(context, existing, removedBy);
                else if (!existing.IsPermanentlyRemoved && wasPermanentlyRemoved)
                    ClearSuppression(context, existing);
            }

            context.Devices.Update(existing);
        }
        else
        {
            var newDevice = _reconciliationService.PrepareNewDevice(incoming);
            if (!isUserAction)
                FederationDeviceIdentity.ApplyLifecycleFields(newDevice, incoming);
            RemapAttachmentParent(context, newDevice);
            newDevice.LastModifiedUtc = _clock.UtcNow;
            context.Devices.Add(newDevice);

            if (!isUserAction && newDevice.IsPermanentlyRemoved)
                RegisterSuppression(context, newDevice, newDevice.RemovedBy ?? incoming.RemovedBy);
        }
    }

    /// <summary>
    /// Applies archive / remove-from-fleet / restore to every Hub row for the same Node + MAC,
    /// regardless of DiscoveryScopeId or device GUID (Hub and Node rows often diverge).
    /// </summary>
    private bool TryApplyFederatedLifecycleToFleet(HubDbContext context, Device incoming)
    {
        var copies = context.Devices
            .Where(d => d.NodeId == incoming.NodeId && d.MacAddress == incoming.MacAddress)
            .ToList();

        if (copies.Count == 0)
            return false;

        var isGovernance = incoming.IsLifecycleGovernancePush
            || copies.Any(c => FederationDeviceIdentity.HasLifecycleDelta(c, incoming));

        if (!isGovernance)
            return false;

        var suppressOnce = false;
        var clearOnce = false;
        string? removedBy = incoming.RemovedBy;

        foreach (var existing in copies)
        {
            var wasPermanentlyRemoved = existing.IsPermanentlyRemoved;

            _reconciliationService.ReconcileExisting(existing, incoming, isUserAction: false);
            existing.LastModifiedUtc = _clock.UtcNow;
            context.Devices.Update(existing);

            if (existing.IsPermanentlyRemoved && !wasPermanentlyRemoved)
                suppressOnce = true;
            if (!existing.IsPermanentlyRemoved && wasPermanentlyRemoved)
                clearOnce = true;

            removedBy ??= existing.RemovedBy;
        }

        var anchor = copies[0];
        if (suppressOnce)
            RegisterSuppression(context, anchor, removedBy ?? incoming.RemovedBy);
        if (clearOnce)
            ClearSuppression(context, anchor);

        _logger.LogInformation(
            "Federated lifecycle applied to {Count} Hub device row(s) for Node {NodeId} MAC {Mac} (deleted={Deleted}, permRemoved={PermRemoved}, removedBy={RemovedBy})",
            copies.Count,
            incoming.NodeId,
            incoming.MacAddress,
            incoming.IsDeleted,
            incoming.IsPermanentlyRemoved,
            removedBy ?? "(none)");

        return true;
    }

    public void UpsertDevices(IEnumerable<Device> devices, bool isUserAction = false)
    {
        if (!devices.Any()) return;

        int retries = 3;
        while (retries > 0)
        {
            try
            {
                using var context = _contextFactory.CreateDbContext();
                // Start a transaction for the entire batch
                using var transaction = context.Database.BeginTransaction();

                foreach (var device in devices)
                {
                    UpsertInternal(context, device, isUserAction);
                }

                context.SaveChanges();
                transaction.Commit();
                return; // Success
            }
            catch (Microsoft.Data.Sqlite.SqliteException ex) when (SqliteConcurrency.IsLockError(ex) && retries > 1)
            {
                retries--;
                Task.Delay(200 * (3 - retries)).Wait(); // Incremental backoff
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Bulk SQLite Upsert failed for {Count} devices.", devices.Count());
                throw;
            }
        }
    }

    private void RegisterSuppression(HubDbContext context, Device device, string? removedBy)
    {
        if (string.IsNullOrWhiteSpace(device.MacAddress)) return;

        if (_suppressions is SqliteDeviceSuppressionRegistry sqlite)
        {
            sqlite.RegisterInContext(
                context,
                _tombstoneGate.ResolveNodeId(device),
                device.MacAddress,
                device.Id,
                removedBy);
            return;
        }

        _tombstoneGate.RegisterSuppression(device, removedBy);
    }

    private void ClearSuppression(HubDbContext context, Device device)
    {
        if (string.IsNullOrWhiteSpace(device.MacAddress)) return;

        if (_suppressions is SqliteDeviceSuppressionRegistry sqlite)
        {
            sqlite.ClearInContext(context, _tombstoneGate.ResolveNodeId(device), device.MacAddress);
            return;
        }

        _tombstoneGate.ClearSuppression(device);
    }

    public List<Device> GetAll(bool includeDeleted = false, bool includePermanentlyRemoved = false) 
    {
        using var context = _contextFactory.CreateDbContext();
        return context.Devices.AsNoTracking()
            .Where(d => (includePermanentlyRemoved || !d.IsPermanentlyRemoved) && (includeDeleted || !d.IsDeleted))
            .ToList();
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
        using var context = _contextFactory.CreateDbContext();
        var query = context.Devices.AsNoTracking()
            .Where(d => !d.IsPermanentlyRemoved && d.IsDeleted == showArchived);

        if (!string.IsNullOrWhiteSpace(nodeId))
        {
            query = query.Where(d => d.NodeId == nodeId);
        }

        if (!string.IsNullOrWhiteSpace(status) && !status.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            if (status.Equals("New", StringComparison.OrdinalIgnoreCase))
            {
                var cutoff = _clock.UtcNow.AddHours(-24);
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
                (d.Client != null && d.Client.ToLower().Contains(s)) ||
                (d.Building != null && d.Building.ToLower().Contains(s)) ||
                (d.Room != null && d.Room.ToLower().Contains(s)) ||
                (d.AttachmentPort != null && d.AttachmentPort.ToLower().Contains(s))
            );
        }

        if (!string.IsNullOrWhiteSpace(client))
        {
            query = query.Where(d => d.Client == client);
        }

        if (!string.IsNullOrWhiteSpace(building))
        {
            query = query.Where(d => d.Building == building);
        }

        if (!string.IsNullOrWhiteSpace(room))
        {
            query = query.Where(d => d.Room == room);
        }

        if (vlanTags != null && vlanTags.Length > 0)
        {
            var includeUntagged = vlanTags.Contains("__untagged__", StringComparer.OrdinalIgnoreCase);
            var explicitTags = vlanTags
                .Where(tag => !tag.Equals("__untagged__", StringComparison.OrdinalIgnoreCase))
                .ToList();

            query = query.Where(d =>
                (includeUntagged && (d.DiscoveryVlanTag == null || d.DiscoveryVlanTag == "")) ||
                (d.DiscoveryVlanTag != null && explicitTags.Contains(d.DiscoveryVlanTag)));
        }

        if (!string.IsNullOrWhiteSpace(attachmentKind)
            && !attachmentKind.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            var kind = attachmentKind.Trim();
            query = query.Where(d =>
                (d.AttachmentKind ?? "Unknown").ToLower() == kind.ToLower());
        }

        if (!string.IsNullOrWhiteSpace(attachmentPort))
        {
            var portNeedle = attachmentPort.Trim().ToLower();
            query = query.Where(d =>
                d.AttachmentPort != null && d.AttachmentPort.ToLower().Contains(portNeedle));
        }

        if (!string.IsNullOrWhiteSpace(attachmentParentId))
        {
            if (attachmentParentId.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(d => d.AttachmentParentDeviceId == null);
            }
            else if (attachmentParentId.Equals("any", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(d => d.AttachmentParentDeviceId != null);
            }
            else if (Guid.TryParse(attachmentParentId, out var parentId))
            {
                query = query.Where(d => d.AttachmentParentDeviceId == parentId);
            }
        }

        totalCount = query.Count();

        return query
            .OrderBy(d => d.FirstSeen)
            .Skip(skip)
            .Take(take)
            .ToList();
    }

    public List<Device> GetAttachmentParentCandidates(string? nodeId, Guid? excludeDeviceId = null, int take = 2000)
    {
        using var context = _contextFactory.CreateDbContext();
        var query = context.Devices.AsNoTracking()
            .Where(d => !d.IsPermanentlyRemoved && !d.IsDeleted);

        if (!string.IsNullOrWhiteSpace(nodeId))
            query = query.Where(d => d.NodeId == nodeId);

        var list = DeviceAttachmentParentCatalog
            .FilterCandidates(query.AsEnumerable(), excludeDeviceId)
            .Take(Math.Clamp(take, 1, 5000))
            .ToList();

        return list;
    }

    public List<string> GetDistinctDiscoveryVlanTags(string? nodeId = null)
    {
        using var context = _contextFactory.CreateDbContext();
        var query = context.Devices.AsNoTracking()
            .Where(d => !d.IsDeleted && !d.IsPermanentlyRemoved && d.DiscoveryVlanTag != null && d.DiscoveryVlanTag != "");

        if (!string.IsNullOrWhiteSpace(nodeId))
            query = query.Where(d => d.NodeId == nodeId);

        return query
            .Select(d => d.DiscoveryVlanTag!)
            .Distinct()
            .OrderBy(tag => tag)
            .ToList();
    }

    public int GetCount() 
    {
        try
        {
            using var context = _contextFactory.CreateDbContext();
            return context.Devices.Count(d => !d.IsDeleted && !d.IsPermanentlyRemoved);
        }
        catch (Exception ex) when (IsSchemaNotReady(ex))
        {
            _logger.LogDebug(ex, "Hub Devices table not ready yet  -  returning count 0.");
            return 0;
        }
    }

    private static bool IsSchemaNotReady(Exception ex) =>
        ex.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase)
        || (ex.InnerException?.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase) ?? false);

    public bool Delete(Guid id)
    {
        using var context = _contextFactory.CreateDbContext();
        var device = context.Devices.FirstOrDefault(d => d.Id == id);
        if (device == null) return false;
        device.IsDeleted = true;
        device.ArchivedUtc ??= _clock.UtcNow;
        device.Status = "offline";
        device.LastModifiedUtc = _clock.UtcNow;
        context.SaveChanges();
        return true;
    }

    public bool Restore(Guid id)
    {
        using var context = _contextFactory.CreateDbContext();
        var device = context.Devices.FirstOrDefault(d => d.Id == id);
        if (device == null || device.IsPermanentlyRemoved) return false;
        device.IsDeleted = false;
        device.ArchivedUtc = null;
        device.LastModifiedUtc = _clock.UtcNow;
        context.SaveChanges();
        return true;
    }

    public bool RemoveFromFleet(Guid id, string? removedBy = null, string? reason = null)
    {
        using var context = _contextFactory.CreateDbContext();
        var device = context.Devices.FirstOrDefault(d => d.Id == id);
        if (device == null) return false;

        device.IsPermanentlyRemoved = true;
        device.IsDeleted = true;
        device.ArchivedUtc ??= _clock.UtcNow;
        device.RemovedUtc = _clock.UtcNow;
        device.RemovedBy = removedBy;
        device.RemovedReason = reason;
        device.LastModifiedUtc = _clock.UtcNow;

        RegisterSuppression(context, device, removedBy);
        context.SaveChanges();
        return true;
    }

    public bool RestoreFromFleet(Guid id)
    {
        using var context = _contextFactory.CreateDbContext();
        var device = context.Devices.FirstOrDefault(d => d.Id == id);
        if (device == null || !device.IsPermanentlyRemoved) return false;

        device.IsPermanentlyRemoved = false;
        device.IsDeleted = false;
        device.ArchivedUtc = null;
        device.RemovedUtc = null;
        device.RemovedBy = null;
        device.RemovedReason = null;
        device.LastModifiedUtc = _clock.UtcNow;

        ClearSuppression(context, device);
        context.SaveChanges();
        return true;
    }

    public List<Device> GetPermanentlyRemoved()
    {
        using var context = _contextFactory.CreateDbContext();
        var devices = context.Devices.AsNoTracking().Where(d => d.IsPermanentlyRemoved).ToList();
        var suppressions = context.DeviceSuppressions.AsNoTracking().ToList();

        foreach (var device in devices)
        {
            if (!string.IsNullOrWhiteSpace(device.RemovedBy))
                continue;

            var mac = DeviceMacNormalizer.Normalize(device.MacAddress);
            var match = suppressions.FirstOrDefault(s => s.DeviceId == device.Id)
                ?? suppressions.FirstOrDefault(s =>
                    s.NodeId == (device.NodeId ?? string.Empty) &&
                    s.NormalizedMac == mac);

            if (!string.IsNullOrWhiteSpace(match?.RemovedBy))
                device.RemovedBy = match.RemovedBy;
        }

        return devices;
    }

    public bool HardDelete(Guid id)
    {
        using var context = _contextFactory.CreateDbContext();
        var device = context.Devices.FirstOrDefault(d => d.Id == id);
        if (device == null) return false;
        if (device.IsPermanentlyRemoved)
            _tombstoneGate.ClearSuppression(device);
        context.Devices.Remove(device);
        context.SaveChanges();
        return true;
    }

    public List<Device> GetDeleted() 
    {
        using var context = _contextFactory.CreateDbContext();
        return context.Devices.AsNoTracking()
            .Where(d => d.IsDeleted && !d.IsPermanentlyRemoved)
            .ToList();
    }

    public List<DeviceRepository.DeviceSummaryItem> GetSummaryData()
    {
        using var context = _contextFactory.CreateDbContext();
        return context.Devices.AsNoTracking()
            .Where(d => !d.IsDeleted && !d.IsPermanentlyRemoved)
            .Select(d => new DeviceRepository.DeviceSummaryItem
            {
                Status = d.Status,
                Vendor = d.Vendor,
                Type = d.Type
            })
            .ToList();
    }

    public bool AcknowledgeSecurityIssue(Guid id, string issue)
    {
        using var context = _contextFactory.CreateDbContext();
        var device = context.Devices.FirstOrDefault(d => d.Id == id);
        if (device == null) return false;

        if (!device.IgnoredSecurityIssues.Contains(issue))
        {
            device.IgnoredSecurityIssues.Add(issue);
            device.LastModifiedUtc = _clock.UtcNow;
            context.SaveChanges();
        }
        return true;
    }

    public bool ResetMetrics(Guid id)
    {
        using var context = _contextFactory.CreateDbContext();
        var device = context.Devices.FirstOrDefault(d => d.Id == id);
        if (device == null) return false;

        device.FlapCount = 0;
        device.LatencyHistory = [];
        device.AverageLatencyMs = 0;
        device.StabilityScore = 100;
        device.CurrentStrikes = 0;
        device.LastModifiedUtc = _clock.UtcNow;

        context.SaveChanges();
        return true;
    }

    private static void RemapAttachmentParent(HubDbContext context, Device device)
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
            var byId = context.Devices.FirstOrDefault(d =>
                d.Id == pid && !d.IsDeleted && !d.IsPermanentlyRemoved);
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
        {
            parent = context.Devices.FirstOrDefault(d =>
                !d.IsDeleted && !d.IsPermanentlyRemoved
                && d.MacAddress == parentMac
                && (string.IsNullOrEmpty(device.NodeId) || d.NodeId == device.NodeId));
        }

        if (parent == null && !string.IsNullOrEmpty(parentIp))
        {
            parent = context.Devices.FirstOrDefault(d =>
                !d.IsDeleted && !d.IsPermanentlyRemoved
                && d.IpAddress == parentIp
                && (string.IsNullOrEmpty(device.NodeId) || d.NodeId == device.NodeId));
        }

        if (parent != null)
        {
            device.AttachmentParentDeviceId = parent.Id;
            var mac = DeviceMacNormalizer.Normalize(parent.MacAddress);
            device.AttachmentParentMac = string.IsNullOrEmpty(mac) ? null : mac;
            device.AttachmentParentIp = string.IsNullOrWhiteSpace(parent.IpAddress) ? parentIp : parent.IpAddress.Trim();
            return;
        }

        if (device.AttachmentParentDeviceId is not Guid stalePid)
            return;

        var local = context.Devices.FirstOrDefault(d =>
            d.Id == stalePid && !d.IsDeleted && !d.IsPermanentlyRemoved);
        if (local == null)
        {
            if (device.IsAttachmentManuallySet)
                return;

            device.AttachmentParentDeviceId = null;
            return;
        }

        var localMac = DeviceMacNormalizer.Normalize(local.MacAddress);
        device.AttachmentParentMac = string.IsNullOrEmpty(localMac) ? null : localMac;
        device.AttachmentParentIp = string.IsNullOrWhiteSpace(local.IpAddress) ? parentIp : local.IpAddress.Trim();
    }

    private Device? FindTombstone(HubDbContext context, Device incoming)
    {
        if (string.IsNullOrWhiteSpace(incoming.MacAddress)) return null;
        var nodeId = _tombstoneGate.ResolveNodeId(incoming);
        return context.Devices.FirstOrDefault(d =>
            d.IsPermanentlyRemoved && d.NodeId == nodeId && d.MacAddress == incoming.MacAddress)
            ?? context.Devices.FirstOrDefault(d =>
                d.IsPermanentlyRemoved && d.MacAddress == incoming.MacAddress);
    }

    private Device? FindExistingByNodeAndMac(HubDbContext context, Device incoming)
    {
        if (string.IsNullOrWhiteSpace(incoming.MacAddress))
            return null;

        var candidates = context.Devices
            .Where(d => d.NodeId == incoming.NodeId && d.MacAddress == incoming.MacAddress)
            .ToList();

        return candidates.FirstOrDefault(d => ScopesCompatible(d.DiscoveryScopeId, incoming.DiscoveryScopeId));
    }

    private Device? FindExistingByNodeAndIp(HubDbContext context, Device incoming)
    {
        if (string.IsNullOrWhiteSpace(incoming.IpAddress))
            return null;

        var candidates = context.Devices
            .Where(d => d.NodeId == incoming.NodeId && d.IpAddress == incoming.IpAddress)
            .ToList();

        return candidates.FirstOrDefault(d => ScopesCompatible(d.DiscoveryScopeId, incoming.DiscoveryScopeId));
    }

    private static bool ScopesCompatible(string? existingScopeId, string? incomingScopeId)
    {
        if (string.IsNullOrWhiteSpace(existingScopeId) || string.IsNullOrWhiteSpace(incomingScopeId))
            return true;

        return string.Equals(existingScopeId, incomingScopeId, StringComparison.OrdinalIgnoreCase);
    }
}
