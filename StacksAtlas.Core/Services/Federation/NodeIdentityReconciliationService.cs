using LiteDB;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StacksAtlas.Core.Abstractions;
using StacksAtlas.Core.Data;
using StacksAtlas.Core.Data.Hub;
using StacksAtlas.Core.Database;
using StacksAtlas.Core.Models;

namespace StacksAtlas.Core.Services.Federation;

public sealed class NodeIdentityReconciliationResult
{
    public bool Success { get; init; } = true;
    public bool Reconciled { get; init; }
    public bool RejectedOnlineConflict { get; init; }
    public string? Message { get; init; }
    public string? RetiredNodeId { get; init; }
    public int DevicesRepointed { get; init; }
    public int DevicesDeduped { get; init; }
}

public interface INodeIdentityReconciliationService
{
    Task<NodeIdentityReconciliationResult> TryReconcileRegistrationAsync(
        string incomingNodeId,
        string? hardwareId,
        string initiatedBy,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Merges stale Hub enrollment rows when the same physical appliance re-enrolls under a new NodeId.
/// Hub appliances only.
/// </summary>
public sealed class NodeIdentityReconciliationService(
    IFederatedNodeRepository nodeRepo,
    IDbContextFactory<HubDbContext> contextFactory,
    LiteDatabase db,
    IAlertEventRepository alertEventRepo,
    IClock clock,
    ILogger<NodeIdentityReconciliationService> logger) : INodeIdentityReconciliationService
{
    public async Task<NodeIdentityReconciliationResult> TryReconcileRegistrationAsync(
        string incomingNodeId,
        string? hardwareId,
        string initiatedBy,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(hardwareId) || string.IsNullOrWhiteSpace(incomingNodeId))
            return new NodeIdentityReconciliationResult();

        var prior = nodeRepo.GetByHardwareId(hardwareId);
        if (prior == null || string.Equals(prior.Id, incomingNodeId, StringComparison.OrdinalIgnoreCase))
            return new NodeIdentityReconciliationResult();

        if (NodeIdentityReconciliationGovernance.IsPriorNodeOnline(prior))
        {
            var message =
                $"This hardware identity is already enrolled as \"{prior.Name ?? prior.Id}\" ({prior.Id}). " +
                "Delete the existing node or re-image this appliance before enrolling.";
            logger.LogWarning(
                "Node identity reconciliation rejected for {IncomingNodeId}: prior {PriorNodeId} is still online.",
                incomingNodeId,
                prior.Id);
            return new NodeIdentityReconciliationResult
            {
                Success = false,
                RejectedOnlineConflict = true,
                Message = message
            };
        }

        return await ReconcileAsync(incomingNodeId, prior.Id, hardwareId, prior, initiatedBy, cancellationToken);
    }

    private async Task<NodeIdentityReconciliationResult> ReconcileAsync(
        string canonicalNodeId,
        string retiredNodeId,
        string hardwareId,
        FederatedNode retiredSnapshot,
        string initiatedBy,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Reconciling Hub node identity {RetiredNodeId} → {CanonicalNodeId} (HardwareId match).",
            retiredNodeId,
            canonicalNodeId);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var devicesRepointed = await context.Devices
            .Where(d => d.NodeId == retiredNodeId)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.NodeId, canonicalNodeId), cancellationToken);

        await context.FederatedLogs
            .Where(l => l.NodeId == retiredNodeId)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.NodeId, canonicalNodeId), cancellationToken);

        await context.AlertEvents
            .Where(a => a.NodeId == retiredNodeId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.NodeId, canonicalNodeId), cancellationToken);

        await context.SystemEvents
            .Where(e => e.NodeId == retiredNodeId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.NodeId, canonicalNodeId), cancellationToken);

        await context.DeviceSuppressions
            .Where(x => x.NodeId == retiredNodeId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.NodeId, canonicalNodeId), cancellationToken);

        var devicesDeduped = DedupeDevicesByMacForNode(context, canonicalNodeId);

        var retired = await context.Nodes.FirstOrDefaultAsync(n => n.Id == retiredNodeId, cancellationToken);
        if (retired == null)
        {
            logger.LogWarning(
                "Node identity reconciliation: retired row {RetiredNodeId} missing during merge (already removed).",
                retiredNodeId);
            return new NodeIdentityReconciliationResult { Reconciled = true, RetiredNodeId = retiredNodeId };
        }

        var canonical = await context.Nodes.FirstOrDefaultAsync(n => n.Id == canonicalNodeId, cancellationToken);
        if (canonical == null)
        {
            canonical = new FederatedNode { Id = canonicalNodeId, HardwareId = hardwareId };
            NodeIdentityReconciliationGovernance.MergeRetiredMetadata(canonical, retiredSnapshot);
            context.Nodes.Add(canonical);
        }
        else
        {
            NodeIdentityReconciliationGovernance.MergeRetiredMetadata(canonical, retiredSnapshot);
            canonical.HardwareId = hardwareId;
            context.Nodes.Update(canonical);
        }

        context.Nodes.Remove(retired);
        await context.SaveChangesAsync(cancellationToken);

        RepointImportedUsers(retiredNodeId, canonicalNodeId);

        alertEventRepo.LogAlert(new AlertEvent
        {
            Id = Guid.NewGuid().ToString(),
            TriggeredAt = clock.UtcNow,
            DeviceId = canonicalNodeId,
            DeviceName = canonical.Name ?? canonicalNodeId,
            DeviceIp = retiredSnapshot.IPAddress ?? string.Empty,
            NodeId = canonicalNodeId,
            NodeName = canonical.Name ?? canonicalNodeId,
            Client = canonical.Client,
            Building = canonical.Building,
            Room = canonical.Room,
            AlertType = AlertEventType.NodeIdentityReconciled,
            Success = true,
            ErrorMessage =
                $"Merged enrollment {retiredNodeId} → {canonicalNodeId} by {initiatedBy}. " +
                $"Repointed {devicesRepointed} device rows; deduped {devicesDeduped} MAC collisions."
        });

        return new NodeIdentityReconciliationResult
        {
            Reconciled = true,
            RetiredNodeId = retiredNodeId,
            DevicesRepointed = devicesRepointed,
            DevicesDeduped = devicesDeduped
        };
    }

    public static int DedupeDevicesByMacForNode(HubDbContext context, string nodeId)
    {
        var devices = context.Devices
            .Where(d => d.NodeId == nodeId && d.MacAddress != null && d.MacAddress != "")
            .ToList();

        var removed = 0;
        foreach (var group in devices
                     .GroupBy(d => d.MacAddress!, StringComparer.OrdinalIgnoreCase)
                     .Where(g => g.Count() > 1))
        {
            var winner = group
                .OrderByDescending(d => d.LastModifiedUtc)
                .ThenByDescending(d => d.IsPermanentlyRemoved)
                .ThenByDescending(d => d.IsDeleted)
                .First();

            foreach (var loser in group.Where(d => d.Id != winner.Id))
            {
                NodeIdentityReconciliationGovernance.MergeStricterLifecycle(winner, loser);
                context.Devices.Remove(loser);
                removed++;
            }
        }

        return removed;
    }

    private void RepointImportedUsers(string retiredNodeId, string canonicalNodeId)
    {
        var userCol = db.GetCollection<User>("users");
        foreach (var user in userCol.Find(u => u.OriginNodeId == retiredNodeId).ToList())
        {
            user.OriginNodeId = canonicalNodeId;
            userCol.Update(user);
        }
    }
}

public sealed class NoOpNodeIdentityReconciliationService : INodeIdentityReconciliationService
{
    public Task<NodeIdentityReconciliationResult> TryReconcileRegistrationAsync(
        string incomingNodeId,
        string? hardwareId,
        string initiatedBy,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new NodeIdentityReconciliationResult());
}
