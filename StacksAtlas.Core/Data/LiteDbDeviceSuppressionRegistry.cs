using LiteDB;
using StacksAtlas.Core.Abstractions;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Services.Devices;

namespace StacksAtlas.Core.Data;

public sealed class LiteDbDeviceSuppressionRegistry : IDeviceSuppressionRegistry
{
    private readonly LiteDatabase _db;
    private readonly IClock _clock;

    public LiteDbDeviceSuppressionRegistry(LiteDatabase db, IClock clock)
    {
        _db = db;
        _clock = clock;
        var col = _db.GetCollection<DeviceSuppression>("device_suppressions");
        col.EnsureIndex(x => x.NodeId);
        col.EnsureIndex(x => x.NormalizedMac);
    }

    private ILiteCollection<DeviceSuppression> Collection =>
        _db.GetCollection<DeviceSuppression>("device_suppressions");

    public bool IsSuppressed(string nodeId, string? macAddress)
    {
        var mac = DeviceMacNormalizer.Normalize(macAddress);
        if (string.IsNullOrEmpty(mac)) return false;
        var key = DeviceMacNormalizer.SuppressionKey(nodeId ?? string.Empty, mac);
        return Collection.FindById(key) != null;
    }

    public void Register(string nodeId, string macAddress, Guid deviceId, string? removedBy)
    {
        var mac = DeviceMacNormalizer.Normalize(macAddress);
        if (string.IsNullOrEmpty(mac)) return;

        var entry = new DeviceSuppression
        {
            Id = DeviceMacNormalizer.SuppressionKey(nodeId ?? string.Empty, mac),
            NodeId = nodeId ?? string.Empty,
            NormalizedMac = mac,
            DeviceId = deviceId,
            SuppressedUtc = _clock.UtcNow,
            RemovedBy = removedBy
        };
        Collection.Upsert(entry);
    }

    public void Clear(string nodeId, string macAddress)
    {
        var mac = DeviceMacNormalizer.Normalize(macAddress);
        if (string.IsNullOrEmpty(mac)) return;
        Collection.Delete(DeviceMacNormalizer.SuppressionKey(nodeId ?? string.Empty, mac));
    }
}
