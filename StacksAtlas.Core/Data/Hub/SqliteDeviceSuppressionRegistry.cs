using Microsoft.EntityFrameworkCore;
using StacksAtlas.Core.Abstractions;
using StacksAtlas.Core.Data.Hub;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Services.Devices;

namespace StacksAtlas.Core.Data.Hub;

public sealed class SqliteDeviceSuppressionRegistry(
    IDbContextFactory<HubDbContext> contextFactory,
    IClock clock) : IDeviceSuppressionRegistry
{
    public bool IsSuppressed(string nodeId, string? macAddress)
    {
        var mac = DeviceMacNormalizer.Normalize(macAddress);
        if (string.IsNullOrEmpty(mac)) return false;

        using var context = contextFactory.CreateDbContext();
        return context.DeviceSuppressions.AsNoTracking()
            .Any(s => s.NodeId == (nodeId ?? string.Empty) && s.NormalizedMac == mac);
    }

    public void Register(string nodeId, string macAddress, Guid deviceId, string? removedBy)
    {
        using var context = contextFactory.CreateDbContext();
        RegisterInContext(context, nodeId, macAddress, deviceId, removedBy);
        context.SaveChanges();
    }

    public void RegisterInContext(
        HubDbContext context,
        string nodeId,
        string macAddress,
        Guid deviceId,
        string? removedBy)
    {
        var mac = DeviceMacNormalizer.Normalize(macAddress);
        if (string.IsNullOrEmpty(mac)) return;

        var id = DeviceMacNormalizer.SuppressionKey(nodeId ?? string.Empty, mac);
        var existing = context.DeviceSuppressions.Find(id);
        if (existing != null)
        {
            existing.DeviceId = deviceId;
            existing.SuppressedUtc = clock.UtcNow;
            existing.RemovedBy = removedBy;
        }
        else
        {
            context.DeviceSuppressions.Add(new DeviceSuppression
            {
                Id = id,
                NodeId = nodeId ?? string.Empty,
                NormalizedMac = mac,
                DeviceId = deviceId,
                SuppressedUtc = clock.UtcNow,
                RemovedBy = removedBy
            });
        }
    }

    public void Clear(string nodeId, string macAddress)
    {
        using var context = contextFactory.CreateDbContext();
        if (ClearInContext(context, nodeId, macAddress))
            context.SaveChanges();
    }

    public bool ClearInContext(HubDbContext context, string nodeId, string macAddress)
    {
        var mac = DeviceMacNormalizer.Normalize(macAddress);
        if (string.IsNullOrEmpty(mac)) return false;

        var id = DeviceMacNormalizer.SuppressionKey(nodeId ?? string.Empty, mac);
        var existing = context.DeviceSuppressions.Find(id);
        if (existing == null) return false;
        context.DeviceSuppressions.Remove(existing);
        return true;
    }
}
