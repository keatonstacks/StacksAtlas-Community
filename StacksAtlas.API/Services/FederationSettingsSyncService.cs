using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using StacksAtlas.API.Hubs;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Settings;
using StacksAtlas.Core.State;
using StacksAtlas.Core.Data;
using StacksAtlas.Core.Database;
using StacksAtlas.Core.Services.Licensing;
using StacksAtlas.Core.Services.Security;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace StacksAtlas.API.Services;

public interface IFederationSettingsSyncService
{
    Task BroadcastSettingsStateAsync();
    Task PushSettingsStateToNodeAsync(string connectionId);
}

public class FederationSettingsSyncService : IFederationSettingsSyncService
{
    private readonly IHubContext<FederationHub> _hubContext;
    private readonly SystemSettingsStore _settings;
    private readonly EmailSettingsRepository _emailRepo;
    private readonly WebhookRepository _webhookRepo;
    private readonly IFederatedNodeRepository _nodeRepo;
    private readonly ILicenseService _licenseService;
    private readonly IHubCertificateAuthority _hubCa;
    private readonly ILogger<FederationSettingsSyncService> _logger;

    public FederationSettingsSyncService(
        IHubContext<FederationHub> hubContext,
        SystemSettingsStore settings,
        EmailSettingsRepository emailRepo,
        WebhookRepository webhookRepo,
        IFederatedNodeRepository nodeRepo,
        ILicenseService licenseService,
        IHubCertificateAuthority hubCa,
        ILogger<FederationSettingsSyncService> logger)
    {
        _hubContext = hubContext;
        _settings = settings;
        _emailRepo = emailRepo;
        _webhookRepo = webhookRepo;
        _nodeRepo = nodeRepo;
        _licenseService = licenseService;
        _hubCa = hubCa;
        _logger = logger;

        _licenseService.OnLicenseStatusChanged += (newStatus) =>
        {
            _ = Task.Run(async () => await PushLicenseToAllNodesAsync(newStatus));
        };
    }

    public async Task BroadcastSettingsStateAsync()
    {
        if (!ExecutionState.IsHub) return;

        try
        {
            _logger.LogInformation("FederationSettingsSync: Generating targeted settings sync payloads...");

            var targetNodes = _nodeRepo.GetAll()
                .Where(n => n.Status == "online" && (n.SyncAlertSettings || n.SyncSiemSettings) && !string.IsNullOrEmpty(n.ConnectionId))
                .ToList();

            if (!targetNodes.Any()) return;

            var emailSettings = _emailRepo.GetSettings();
            var webhooks = _webhookRepo.GetAll();
            var systemSettings = _settings.Load();

            foreach (var node in targetNodes)
            {
                try
                {
                    var payload = new FederatedSettingsPayload
                    {
                        EmailSettings = (node.SyncAlertSettings && !node.OverrideAlertSettings) ? emailSettings : null,
                        Webhooks = (node.SyncAlertSettings && !node.OverrideAlertSettings) ? webhooks : null,
                        SyslogEnabled = (node.SyncSiemSettings && !node.OverrideSiemSettings) ? systemSettings.SyslogEnabled : null,
                        SyslogHost = (node.SyncSiemSettings && !node.OverrideSiemSettings) ? systemSettings.SyslogHost : null,
                        SyslogPort = (node.SyncSiemSettings && !node.OverrideSiemSettings) ? systemSettings.SyslogPort : null,
                        SyslogAppName = (node.SyncSiemSettings && !node.OverrideSiemSettings) ? systemSettings.SyslogAppName : null
                    };

                    await _hubContext.Clients.Client(node.ConnectionId!).SendAsync("SyncSettings", payload);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "FederationSettingsSync: Failed to dispatch settings payload to Node {NodeId}", node.Id);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FederationSettingsSync: Error broadcasting settings state.");
        }
    }

    public async Task PushSettingsStateToNodeAsync(string connectionId)
    {
        if (!ExecutionState.IsHub) return;

        try
        {
            var node = _nodeRepo.GetAll().FirstOrDefault(n => n.ConnectionId == connectionId);
            if (node == null)
            {
                _logger.LogWarning("FederationSettingsSync: Could not find node with ConnectionId {ConnectionId} to push settings.", connectionId);
                return;
            }

            if (!node.SyncAlertSettings && !node.SyncSiemSettings)
            {
                return;
            }

            var payload = new FederatedSettingsPayload
            {
                EmailSettings = (node.SyncAlertSettings && !node.OverrideAlertSettings) ? _emailRepo.GetSettings() : null,
                Webhooks = (node.SyncAlertSettings && !node.OverrideAlertSettings) ? _webhookRepo.GetAll() : null,
                SyslogEnabled = (node.SyncSiemSettings && !node.OverrideSiemSettings) ? _settings.Load().SyslogEnabled : null,
                SyslogHost = (node.SyncSiemSettings && !node.OverrideSiemSettings) ? _settings.Load().SyslogHost : null,
                SyslogPort = (node.SyncSiemSettings && !node.OverrideSiemSettings) ? _settings.Load().SyslogPort : null,
                SyslogAppName = (node.SyncSiemSettings && !node.OverrideSiemSettings) ? _settings.Load().SyslogAppName : null
            };

            await _hubContext.Clients.Client(connectionId).SendAsync("SyncSettings", payload);
            _logger.LogInformation("FederationSettingsSync: Settings synced to Node {NodeId}", node.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FederationSettingsSync: Error syncing settings to connection {ConnectionId}", connectionId);
        }
    }

    private async Task PushLicenseToAllNodesAsync(LicenseStatus licenseStatus)
    {
        if (!ExecutionState.IsHub) return;

        try
        {
            var targetNodes = _nodeRepo.GetAll()
                .Where(n => n.Status == "online" && !string.IsNullOrEmpty(n.ConnectionId))
                .ToList();

            if (!targetNodes.Any()) return;

            _logger.LogInformation("FederationSettingsSync: Broadcasting license status change to {Count} online Nodes. New Tier: {Tier}", targetNodes.Count, licenseStatus.Tier);

            foreach (var node in targetNodes)
            {
                try
                {
                    var payload = new StacksAtlas.Core.Services.Licensing.InheritedLicensePayload(
                        node.Id,
                        licenseStatus.Tier.ToString(),
                        DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                    );
                    
                    var payloadJson = System.Text.Json.JsonSerializer.Serialize(payload);
                    var payloadBytes = System.Text.Encoding.UTF8.GetBytes(payloadJson);
                    var signature = _hubCa.SignData(payloadBytes);

                    var command = new FederationCommand
                    {
                        CommandType = "UpdateInheritedLicense",
                        Parameters = new Dictionary<string, string>
                        {
                            { "PayloadJson", payloadJson },
                            { "Signature", signature }
                        }
                    };

                    await _hubContext.Clients.Client(node.ConnectionId!).SendAsync("ExecuteCommand", command);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "FederationSettingsSync: Failed to push updated license to Node {NodeId}", node.Id);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FederationSettingsSync: Error broadcasting license update.");
        }
    }
}
