using Microsoft.Extensions.Logging;
using StacksAtlas.Core.Abstractions;
using StacksAtlas.Core.Data;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Services.Devices;
using StacksAtlas.Core.Settings;

using StacksAtlas.Core.State;

namespace StacksAtlas.Core.Services.Devices;

/// <summary>
/// Discovery gate and rediscovery logging for permanently removed devices (§7.9 Phase B).
/// </summary>
public sealed class DeviceTombstoneGate(
    IDeviceSuppressionRegistry suppressions,
    IClock clock,
    FederationSettingsStore? settingsStore = null,
    ILogger<DeviceTombstoneGate>? logger = null,
    StacksAtlas.Core.Data.IEventRepository? eventRepository = null)
{
    private readonly IDeviceSuppressionRegistry _suppressions = suppressions;
    private readonly IClock _clock = clock;
    private readonly FederationSettingsStore? _settingsStore = settingsStore;
    private readonly ILogger<DeviceTombstoneGate>? _logger = logger;
    private readonly StacksAtlas.Core.Data.IEventRepository? _eventRepository = eventRepository;

    public string ResolveNodeId(Device incoming)
    {
        if (!string.IsNullOrWhiteSpace(incoming.NodeId))
            return incoming.NodeId;
        return _settingsStore?.Current.NodeId ?? string.Empty;
    }

    /// <summary>
    /// Returns true when a non-user discovery upsert should be blocked (tombstone hit or suppression).
    /// </summary>
    public bool ShouldBlockDiscoveryUpsert(
        Device? existing,
        Device incoming,
        bool isUserAction,
        out Device? tombstoneToUpdate)
    {
        tombstoneToUpdate = null;
        if (isUserAction) return false;

        // Federated lifecycle telemetry from Node (Hub ingest only  -  Nodes never run discovery against Hub).
        if (existing != null && FederationDeviceIdentity.SameFleetDevice(existing, incoming))
        {
            if (!existing.IsPermanentlyRemoved && incoming.IsPermanentlyRemoved)
                return false; // remove-from-fleet

            if (existing.IsPermanentlyRemoved && incoming.IsPermanentlyRemoved)
                return false; // tombstone metadata (RemovedBy, rediscovery counts)

            if (!existing.IsPermanentlyRemoved && !incoming.IsPermanentlyRemoved
                && incoming.IsDeleted != existing.IsDeleted)
                return false; // archive / unarchive

            if (ExecutionState.IsHub
                && existing.IsPermanentlyRemoved
                && !incoming.IsPermanentlyRemoved)
                return false; // restore-to-fleet from Node
        }

        var nodeId = ResolveNodeId(incoming);
        var mac = DeviceMacNormalizer.Normalize(incoming.MacAddress);

        if (existing?.IsPermanentlyRemoved == true)
        {
            tombstoneToUpdate = existing;
            return true;
        }

        if (!string.IsNullOrEmpty(mac) && _suppressions.IsSuppressed(nodeId, mac))
        {
            if (existing?.IsPermanentlyRemoved == true)
                tombstoneToUpdate = existing;
            return true;
        }

        return false;
    }

    public void ApplyRediscoveryHit(Device tombstone, string? ipAddress)
    {
        var now = _clock.UtcNow;
        tombstone.LastRediscoveryAttemptUtc = now;
        tombstone.LastRediscoveryIp = ipAddress;
        tombstone.RediscoveryHitCount++;
        tombstone.LastModifiedUtc = now;

        var shouldLog = tombstone.RediscoveryHitCount == 1
            || tombstone.RediscoveryHitCount % 10 == 0
            || !tombstone.LastRediscoveryAttemptUtc.HasValue; // unreachable but safe

        if (shouldLog)
        {
            _logger?.LogInformation(
                "Permanently removed device rediscovered (hit {HitCount}): Node {NodeId}, MAC {Mac}, IP {Ip}",
                tombstone.RediscoveryHitCount,
                tombstone.NodeId,
                tombstone.MacAddress,
                ipAddress);

            _eventRepository?.AddEvent(new SystemEvent
            {
                Type = "RediscoveryHit",
                Severity = "Warning",
                Message = $"Removed device seen again (hit {tombstone.RediscoveryHitCount}): {tombstone.Name ?? tombstone.MacAddress ?? tombstone.IpAddress} at {ipAddress}",
                DeviceIp = ipAddress ?? tombstone.IpAddress,
                NodeId = tombstone.NodeId,
                Timestamp = now
            });
        }
    }

    public void RegisterSuppression(Device device, string? removedBy)
    {
        if (string.IsNullOrWhiteSpace(device.MacAddress)) return;
        _suppressions.Register(ResolveNodeId(device), device.MacAddress, device.Id, removedBy);
    }

    public void ClearSuppression(Device device)
    {
        if (string.IsNullOrWhiteSpace(device.MacAddress)) return;
        _suppressions.Clear(ResolveNodeId(device), device.MacAddress);
    }
}
