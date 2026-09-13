using LiteDB;
using Microsoft.EntityFrameworkCore;
using StacksAtlas.Core.Data.Hub;
using StacksAtlas.Core.Models;

namespace StacksAtlas.Core.Services.Federation;

public sealed class NodeFleetTelemetryPurgeResult
{
    public int DevicesRemoved { get; init; }
    public int LogsRemoved { get; init; }
    public int AlertsRemoved { get; init; }
    public int SystemEventsRemoved { get; init; }
    public int SuppressionsRemoved { get; init; }
    public int UsersRemoved { get; init; }

    public int TotalRemoved =>
        DevicesRemoved + LogsRemoved + AlertsRemoved + SystemEventsRemoved + SuppressionsRemoved + UsersRemoved;
}

public interface INodeFleetTelemetryPurgeService
{
    Task<NodeFleetTelemetryPurgeResult> PurgeAsync(
        string nodeId,
        string? legacyNodeName = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Purges Hub fleet telemetry for a node while keeping the enrollment record.
/// Hub appliances only  -  registered when StacksAtlas.Hub.db is present.
/// </summary>
public sealed class NodeFleetTelemetryPurgeService(
    IDbContextFactory<HubDbContext> contextFactory,
    LiteDatabase db) : INodeFleetTelemetryPurgeService
{
    public async Task<NodeFleetTelemetryPurgeResult> PurgeAsync(
        string nodeId,
        string? legacyNodeName = null,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var devices = await context.Devices
            .Where(d => d.NodeId == nodeId || (legacyNodeName != null && d.NodeId == legacyNodeName))
            .ToListAsync(cancellationToken);

        var logs = await context.FederatedLogs
            .Where(l => l.NodeId == nodeId || (legacyNodeName != null && l.NodeId == legacyNodeName))
            .ToListAsync(cancellationToken);

        var alerts = await context.AlertEvents
            .Where(a => a.NodeId == nodeId || (legacyNodeName != null && a.NodeId == legacyNodeName))
            .ToListAsync(cancellationToken);

        var systemEvents = await context.SystemEvents
            .Where(e => e.NodeId == nodeId || (legacyNodeName != null && e.NodeId == legacyNodeName))
            .ToListAsync(cancellationToken);

        var suppressions = await context.DeviceSuppressions
            .Where(s => s.NodeId == nodeId || (legacyNodeName != null && s.NodeId == legacyNodeName))
            .ToListAsync(cancellationToken);

        if (devices.Count > 0) context.Devices.RemoveRange(devices);
        if (logs.Count > 0) context.FederatedLogs.RemoveRange(logs);
        if (alerts.Count > 0) context.AlertEvents.RemoveRange(alerts);
        if (systemEvents.Count > 0) context.SystemEvents.RemoveRange(systemEvents);
        if (suppressions.Count > 0) context.DeviceSuppressions.RemoveRange(suppressions);

        await context.SaveChangesAsync(cancellationToken);

        var usersRemoved = PurgeImportedUsers(db, nodeId);

        return new NodeFleetTelemetryPurgeResult
        {
            DevicesRemoved = devices.Count,
            LogsRemoved = logs.Count,
            AlertsRemoved = alerts.Count,
            SystemEventsRemoved = systemEvents.Count,
            SuppressionsRemoved = suppressions.Count,
            UsersRemoved = usersRemoved
        };
    }

    private static int PurgeImportedUsers(LiteDatabase db, string nodeId)
    {
        var userCol = db.GetCollection<User>("users");
        var usersToDelete = userCol.Find(u => u.OriginNodeId == nodeId).ToList();
        foreach (var user in usersToDelete)
            userCol.Delete(user.Id);

        return usersToDelete.Count;
    }
}

/// <summary>Nodes and standalones  -  site reset purge is Hub-only.</summary>
public sealed class NoOpNodeFleetTelemetryPurgeService : INodeFleetTelemetryPurgeService
{
    public Task<NodeFleetTelemetryPurgeResult> PurgeAsync(
        string nodeId,
        string? legacyNodeName = null,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Fleet telemetry purge is only available on a Hub appliance.");
}
