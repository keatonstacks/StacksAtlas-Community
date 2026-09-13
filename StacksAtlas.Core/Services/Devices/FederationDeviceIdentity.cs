using StacksAtlas.Core.Models;

namespace StacksAtlas.Core.Services.Devices;

/// <summary>
/// Fleet device identity for Hub ↔ Node reconciliation (NodeId + normalized MAC).
/// Hub and Node rows often have different GUIDs for the same physical device.
/// </summary>
public static class FederationDeviceIdentity
{
    public static string NormalizeMac(string? mac) =>
        DeviceMacNormalizer.Normalize(mac);

    public static bool SameFleetDevice(Device? a, Device? b)
    {
        if (a == null || b == null) return false;
        if (a.Id == b.Id) return true;

        if (string.IsNullOrWhiteSpace(a.NodeId) || a.NodeId != b.NodeId)
            return false;

        var macA = NormalizeMac(a.MacAddress);
        var macB = NormalizeMac(b.MacAddress);
        return !string.IsNullOrEmpty(macA) && macA == macB;
    }

    public static bool HasLifecycleDelta(Device existing, Device incoming) =>
        incoming.IsPermanentlyRemoved != existing.IsPermanentlyRemoved
        || incoming.IsDeleted != existing.IsDeleted;

    /// <summary>Apply archive / tombstone / restore fields from Node telemetry onto a Hub row.</summary>
    public static void ApplyLifecycleFields(Device target, Device incoming)
    {
        if (incoming.IsPermanentlyRemoved != target.IsPermanentlyRemoved)
        {
            target.IsPermanentlyRemoved = incoming.IsPermanentlyRemoved;
            if (incoming.IsPermanentlyRemoved)
            {
                target.IsDeleted = true;
                target.ArchivedUtc ??= incoming.ArchivedUtc ?? target.ArchivedUtc;
                target.RemovedUtc = incoming.RemovedUtc ?? target.RemovedUtc;
                target.RemovedBy = incoming.RemovedBy ?? target.RemovedBy;
                target.RemovedReason = incoming.RemovedReason ?? target.RemovedReason;
            }
            else
            {
                target.IsDeleted = incoming.IsDeleted;
                if (!incoming.IsDeleted)
                    target.ArchivedUtc = null;
                target.RemovedUtc = null;
                target.RemovedBy = null;
                target.RemovedReason = null;
                target.RediscoveryHitCount = 0;
                target.LastRediscoveryAttemptUtc = null;
                target.LastRediscoveryIp = null;
            }
        }
        else if (incoming.IsDeleted != target.IsDeleted)
        {
            target.IsDeleted = incoming.IsDeleted;
            if (incoming.IsDeleted)
                target.ArchivedUtc ??= incoming.ArchivedUtc ?? target.ArchivedUtc;
            else
                target.ArchivedUtc = null;
        }

        if (incoming.ArchivedUtc.HasValue && target.IsDeleted)
            target.ArchivedUtc ??= incoming.ArchivedUtc;

        if (!target.IsPermanentlyRemoved)
            return;

        if (incoming.RediscoveryHitCount > target.RediscoveryHitCount)
            target.RediscoveryHitCount = incoming.RediscoveryHitCount;

        if (incoming.LastRediscoveryAttemptUtc.HasValue)
            target.LastRediscoveryAttemptUtc = incoming.LastRediscoveryAttemptUtc;

        if (!string.IsNullOrWhiteSpace(incoming.LastRediscoveryIp))
            target.LastRediscoveryIp = incoming.LastRediscoveryIp;

        if (!string.IsNullOrWhiteSpace(incoming.RemovedBy))
            target.RemovedBy = incoming.RemovedBy;

        if (incoming.RemovedUtc.HasValue)
            target.RemovedUtc = incoming.RemovedUtc;
    }
}
