using StacksAtlas.API.Hubs;
using StacksAtlas.Core.Abstractions;
using StacksAtlas.Core.Data;
using StacksAtlas.Core.Services.Governance;
using StacksAtlas.Core.State;

namespace StacksAtlas.API.Workers;

/// <summary>
/// Hub-only: reconcile stale federated node rows when SignalR drops without a clean disconnect.
/// </summary>
public sealed class FederationNodeLivenessWorker(
    IFederatedNodeRepository nodeRepo,
    IClock clock,
    ILogger<FederationNodeLivenessWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan StaleGrace = TimeSpan.FromMinutes(3);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!ExecutionState.IsHubBrainEnabled)
            return;

        logger.LogInformation("FederationNodeLivenessWorker: Hub node liveness reconciliation started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(PollInterval, stoppingToken);
                ReconcileStaleNodes();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "FederationNodeLivenessWorker: reconciliation pass failed.");
            }
        }
    }

    private void ReconcileStaleNodes()
    {
        var utcNow = clock.UtcNow;
        foreach (var node in nodeRepo.GetAll())
        {
            if (FederatedNodeStatus.ShouldPreserveOnHeartbeat(node.Status))
                continue;

            var signalRConnected = FederationSignalRRegistry.IsNodeConnected(node.Id, node.ConnectionId);
            var effective = FederatedNodeStatus.ResolveEffectiveStatus(node, signalRConnected, utcNow);

            if (string.Equals(node.Status, effective, StringComparison.OrdinalIgnoreCase)
                && (signalRConnected || string.IsNullOrEmpty(node.ConnectionId)))
            {
                continue;
            }

            if (string.Equals(effective, FederatedNodeStatus.Offline, StringComparison.OrdinalIgnoreCase))
            {
                node.Status = FederatedNodeStatus.Offline;
                node.ConnectionId = null;
                nodeRepo.UpsertNode(node);
                logger.LogInformation(
                    "FederationNodeLivenessWorker: Marked Node {NodeId} ({Name}) offline (last seen {LastSeenUtc:u}).",
                    node.Id,
                    node.Name,
                    node.LastSeenUtc);
            }
        }
    }
}
