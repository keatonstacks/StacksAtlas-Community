using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using StacksAtlas.API.Hubs;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Services.Auth;
using StacksAtlas.Core.Settings;
using StacksAtlas.Core.State;
using StacksAtlas.Core.Data;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace StacksAtlas.API.Services;

public interface IFederationIdentitySyncService
{
    Task BroadcastIdentityStateAsync();
    Task PushIdentityStateToNodeAsync(string connectionId);
}

public class FederationIdentitySyncService : IFederationIdentitySyncService
{
    private readonly IHubContext<FederationHub> _hubContext;
    private readonly IAuthService _auth;
    private readonly SystemSettingsStore _settings;
    private readonly IFederatedNodeRepository _nodeRepo;
    private readonly ILogger<FederationIdentitySyncService> _logger;

    public FederationIdentitySyncService(
        IHubContext<FederationHub> hubContext,
        IAuthService auth,
        SystemSettingsStore settings,
        IFederatedNodeRepository nodeRepo,
        ILogger<FederationIdentitySyncService> logger)
    {
        _hubContext = hubContext;
        _auth = auth;
        _settings = settings;
        _nodeRepo = nodeRepo;
        _logger = logger;
    }

    public async Task BroadcastIdentityStateAsync()
    {
        if (!ExecutionState.IsHub) return;

        try
        {
            _logger.LogInformation("FederationIdentitySync: Generating targeted identity sync payloads...");

            // Only broadcast to connected nodes that have user or SSO sync enabled
            var targetNodes = _nodeRepo.GetAll()
                .Where(n => n.Status == "online" && (n.SyncUsers || n.SyncUserRegistry || n.SyncSsoSettings) && !string.IsNullOrEmpty(n.ConnectionId))
                .ToList();

            _logger.LogInformation("FederationIdentitySync: Broadcasting targeted identity payload to {Count} nodes.", targetNodes.Count);
            
            var allUsers = _auth.GetAllUsers();
            var allAuthSettings = _settings.Load().Auth;

            foreach (var node in targetNodes)
            {
                try
                {
                    var payload = new FederatedIdentityPayload
                    {
                        Users = (node.SyncUsers || node.SyncUserRegistry) ? allUsers : null,
                        AuthSettings = (node.SyncUsers || node.SyncSsoSettings) ? allAuthSettings : null
                    };

                    await _hubContext.Clients.Client(node.ConnectionId!).SendAsync("SyncIdentity", payload);
                    _logger.LogInformation("FederationIdentitySync: Dispatched identity payload to Node {NodeId} ({ConnectionId}) (Users: {SyncUsers}, SSO: {SyncSso})", 
                        node.Id, node.ConnectionId, node.SyncUsers || node.SyncUserRegistry, node.SyncUsers || node.SyncSsoSettings);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "FederationIdentitySync: Failed to dispatch identity payload to Node {NodeId}", node.Id);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FederationIdentitySync: Error broadcasting identity state.");
        }
    }

    public async Task PushIdentityStateToNodeAsync(string connectionId)
    {
        if (!ExecutionState.IsHub) return;

        try
        {
            var node = _nodeRepo.GetAll().FirstOrDefault(n => n.ConnectionId == connectionId);
            if (node == null)
            {
                _logger.LogWarning("FederationIdentitySync: Could not find node with ConnectionId {ConnectionId} to push initial state.", connectionId);
                return;
            }

            if (!node.SyncUsers && !node.SyncUserRegistry && !node.SyncSsoSettings)
            {
                _logger.LogInformation("FederationIdentitySync: IAM/SSO sync is disabled for Node {NodeId} ({ConnectionId}). Skipping initial identity push.", node.Id, connectionId);
                return;
            }

            _logger.LogInformation("FederationIdentitySync: Pushing fresh identity payload to connection {ConnectionId} for Node {NodeId}", connectionId, node.Id);
            var payload = new FederatedIdentityPayload
            {
                Users = (node.SyncUsers || node.SyncUserRegistry) ? _auth.GetAllUsers() : null,
                AuthSettings = (node.SyncUsers || node.SyncSsoSettings) ? _settings.Load().Auth : null
            };

            await _hubContext.Clients.Client(connectionId).SendAsync("SyncIdentity", payload);
            _logger.LogInformation("FederationIdentitySync: Initial identity state synced to {ConnectionId} for Node {NodeId} (Users: {SyncUsers}, SSO: {SyncSso})", 
                connectionId, node.Id, node.SyncUsers || node.SyncUserRegistry, node.SyncUsers || node.SyncSsoSettings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FederationIdentitySync: Error syncing initial identity state to connection {ConnectionId}", connectionId);
        }
    }
}
