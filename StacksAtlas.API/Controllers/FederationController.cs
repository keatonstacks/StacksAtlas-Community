using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Mvc;
using StacksAtlas.Core.Data;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.State;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using StacksAtlas.Core.Database;
using StacksAtlas.Core.Abstractions;
using StacksAtlas.Core.Services.Security;
using StacksAtlas.Core.Services.Licensing;
using StacksAtlas.Core.Helpers;

using StacksAtlas.Core.Services.Updates;
using StacksAtlas.API.Services;
using StacksAtlas.API.Hubs;
using StacksAtlas.API.Workers;
using StacksAtlas.Core.Services.Federation;
using StacksAtlas.Core.Services.Governance;
using StacksAtlas.Core.Services.Scanning;
using StacksAtlas.Core.Services.Audit;

namespace StacksAtlas.API.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class FederationController : ControllerBase
{
    private readonly IFederatedNodeRepository _nodeRepo;
    private readonly IDeviceRepository _deviceRepo;
    private readonly StacksAtlas.Core.Settings.FederationSettingsStore _settingsStore;
    private readonly Microsoft.AspNetCore.SignalR.IHubContext<StacksAtlas.API.Hubs.FederationHub> _hubContext;
    private readonly IFederationIdentitySyncService _identitySync;
    private readonly IFederationSettingsSyncService _settingsSync;
    private readonly LiteDB.LiteDatabase _db;
    private readonly ILogger<FederationController> _logger;

    private readonly IDbContextFactory<StacksAtlas.Core.Data.Hub.HubDbContext>? _contextFactory;
    private readonly IHubCertificateAuthority _hubCa;
    private readonly EnrollmentTokenManager _tokenManager;
    private readonly ILicenseService _licenseService;
    private readonly ITailscaleStatusService _tailscaleStatusService;
    private readonly ITailscaleReachabilityService _tailscaleReachabilityService;
    private readonly INetworkInterfaceService _networkInterfaceService;
    private readonly StacksAtlas.Core.Settings.NetworkSettingsStore _networkSettingsStore;
    private readonly StacksAtlas.Core.Settings.SystemSettingsStore _systemSettingsStore;
    private readonly INodeFleetTelemetryPurgeService _telemetryPurge;
    private readonly INodeIdentityReconciliationService _identityReconciliation;
    private readonly IAlertEventRepository _alertEventRepo;
    private readonly IClock _clock;
    private readonly IAuditService _audit;
    private readonly FederationNodePulseService _nodePulseService;
    private readonly IUpdateDepotService? _updateDepotService;

    public FederationController(
        IFederatedNodeRepository nodeRepo, 
        IDeviceRepository deviceRepo,
        StacksAtlas.Core.Settings.FederationSettingsStore settingsStore,
        StacksAtlas.Core.Settings.NetworkSettingsStore networkSettingsStore,
        StacksAtlas.Core.Settings.SystemSettingsStore systemSettingsStore,
        Microsoft.AspNetCore.SignalR.IHubContext<StacksAtlas.API.Hubs.FederationHub> hubContext,
        IFederationIdentitySyncService identitySync,
        IFederationSettingsSyncService settingsSync,
        LiteDB.LiteDatabase db,
        ILogger<FederationController> logger,

        IHubCertificateAuthority hubCa,
        EnrollmentTokenManager tokenManager,
        ILicenseService licenseService,
        ITailscaleStatusService tailscaleStatusService,
        ITailscaleReachabilityService tailscaleReachabilityService,
        INetworkInterfaceService networkInterfaceService,
        INodeFleetTelemetryPurgeService telemetryPurge,
        INodeIdentityReconciliationService identityReconciliation,
        IAlertEventRepository alertEventRepo,
        IClock clock,
        IAuditService audit,
        FederationNodePulseService nodePulseService,
        IDbContextFactory<StacksAtlas.Core.Data.Hub.HubDbContext>? contextFactory = null,
        IUpdateDepotService? updateDepotService = null)
    {
        _nodeRepo = nodeRepo;
        _deviceRepo = deviceRepo;
        _settingsStore = settingsStore;
        _hubContext = hubContext;
        _identitySync = identitySync;
        _settingsSync = settingsSync;
        _db = db;
        _logger = logger;

        _hubCa = hubCa;
        _tokenManager = tokenManager;
        _licenseService = licenseService;
        _tailscaleStatusService = tailscaleStatusService;
        _tailscaleReachabilityService = tailscaleReachabilityService;
        _networkInterfaceService = networkInterfaceService;
        _audit = audit;
        _networkSettingsStore = networkSettingsStore;
        _systemSettingsStore = systemSettingsStore;
        _telemetryPurge = telemetryPurge;
        _identityReconciliation = identityReconciliation;
        _alertEventRepo = alertEventRepo;
        _clock = clock;
        _contextFactory = contextFactory;
        _nodePulseService = nodePulseService;
        _updateDepotService = updateDepotService;
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("nodes/{id}/command")]
    public async Task<IActionResult> TriggerCommand(string id, [FromBody] FederationCommand command, CancellationToken cancellationToken)
    {
        if (!ExecutionState.IsHub)
            return BadRequest("This endpoint is only available in Hub mode.");

        var node = _nodeRepo.GetById(id);
        if (node == null) return NotFound("Node not found.");

        var nodeOnline = !string.IsNullOrWhiteSpace(node.ConnectionId);
        var isForceSync = string.Equals(command.CommandType, "ForceSync", StringComparison.OrdinalIgnoreCase);
        FederationNodePulseResult? pulseResult = null;

        if (!nodeOnline || isForceSync)
        {
            pulseResult = await _nodePulseService.TryPulseAsync(node, cancellationToken);
            if (pulseResult.Success && !nodeOnline)
            {
                for (var attempt = 0; attempt < 12 && !nodeOnline; attempt++)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                    node = _nodeRepo.GetById(id) ?? node;
                    nodeOnline = !string.IsNullOrWhiteSpace(node.ConnectionId);
                }
            }
        }

        await _hubContext.Clients.Group($"Node_{id}").SendAsync("ExecuteCommand", command, cancellationToken);

        if (nodeOnline)
        {
            return Ok(new
            {
                message = $"Command {command.CommandType} transmitted to Node {id}.",
                nodeOnline = true,
                pulseSent = pulseResult?.Success ?? false,
            });
        }

        if (pulseResult?.Success == true)
        {
            return Ok(new
            {
                message = $"Node {id} was offline. Wake-up pulse accepted; command {command.CommandType} sent when SignalR reconnects.",
                nodeOnline = false,
                pulseSent = true,
            });
        }

        return Ok(new
        {
            message = $"Node {id} appears offline. Command {command.CommandType} queued on Hub; pulse failed ({pulseResult?.Detail ?? "unknown"}).",
            nodeOnline = false,
            pulseSent = false,
        });
    }

    /// <summary>
    /// Notify an enrolled Node that a Hub-staged update is available (consent-gated apply on Node).
    /// </summary>
    [Authorize(Roles = "Admin")]
    [HttpPost("nodes/{id}/notify-update")]
    public async Task<IActionResult> NotifyNodeUpdate(
        string id,
        [FromQuery] string? channel = null,
        CancellationToken cancellationToken = default)
    {
        return await TriggerNodeUpdateInternal(id, requestedApply: false, channel, cancellationToken);
    }

    /// <summary>
    /// Ask an enrolled Node to prepare a Hub-staged update. Node still requires local admin confirm to apply.
    /// </summary>
    [Authorize(Roles = "Admin")]
    [HttpPost("nodes/{id}/trigger-update")]
    public async Task<IActionResult> TriggerNodeUpdate(
        string id,
        [FromQuery] string? channel = null,
        CancellationToken cancellationToken = default)
    {
        return await TriggerNodeUpdateInternal(id, requestedApply: true, channel, cancellationToken);
    }

    private async Task<IActionResult> TriggerNodeUpdateInternal(
        string id,
        bool requestedApply,
        string? channel,
        CancellationToken cancellationToken)
    {
        if (!ExecutionState.IsHub)
            return BadRequest("This endpoint is only available in Hub mode.");

        if (_updateDepotService is null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "Update depot unavailable." });

        var node = _nodeRepo.GetById(id);
        if (node is null)
            return NotFound("Node not found.");

        if (string.IsNullOrWhiteSpace(node.ConnectionId))
        {
            return Conflict(new
            {
                message = $"Node \"{node.Name ?? id}\" is offline. Update notify requires an active connection."
            });
        }

        var resolvedChannel = UpdateDepotPaths.NormalizeChannel(channel);
        if (!_updateDepotService.TryGetLatestStagedRelease(resolvedChannel, out var version, out _, out _))
        {
            return BadRequest(new
            {
                message = $"No update package is staged on the Hub depot for channel '{resolvedChannel}'."
            });
        }

        if (!UpdateFleetPolicy.IsActionableOffer(version, node.Version))
        {
            return BadRequest(new
            {
                message =
                    $"Node \"{node.Name ?? id}\" is already on v{(string.IsNullOrWhiteSpace(node.Version) ? "unknown" : node.Version)}; " +
                    $"staged depot release is v{version}. Notify only when the site is behind."
            });
        }

        var initiatedBy = User.Identity?.Name ?? "Hub Admin";
        var commandType = requestedApply ? "TriggerUpdate" : "NotifyUpdateAvailable";
        var command = new FederationCommand
        {
            CommandType = commandType,
            TargetId = id,
            Parameters = new Dictionary<string, string>
            {
                ["channel"] = resolvedChannel,
                ["availableVersion"] = version,
                ["initiatedBy"] = initiatedBy,
                ["requestedApply"] = requestedApply ? "true" : "false",
                ["message"] = requestedApply
                    ? $"Hub requested update to v{version}. Confirm apply in Software Updates."
                    : $"Hub staged update v{version} is available."
            }
        };

        FederationCommandResult? result;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            result = await _hubContext.Clients.Client(node.ConnectionId)
                .InvokeAsync<FederationCommandResult>("ExecuteCommand", command, timeout.Token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Hub: failed to deliver {CommandType} to Node {NodeId}", commandType, id);
            _audit.Record(
                requestedApply ? AuditActions.UpdatesFleetTrigger : AuditActions.UpdatesFleetNotify,
                "federation_node",
                id,
                AuditOutcomes.Failed,
                detail: $"channel={resolvedChannel}; version={version}; error={ex.Message}");
            return BadRequest(new { message = $"Failed to reach Node: {ex.Message}" });
        }

        if (result?.Success != true)
        {
            _audit.Record(
                requestedApply ? AuditActions.UpdatesFleetTrigger : AuditActions.UpdatesFleetNotify,
                "federation_node",
                id,
                AuditOutcomes.Failed,
                detail: $"channel={resolvedChannel}; version={version}; message={result?.Message}");
            return BadRequest(new { message = result?.Message ?? "Node rejected update command." });
        }

        _audit.Record(
            requestedApply ? AuditActions.UpdatesFleetTrigger : AuditActions.UpdatesFleetNotify,
            "federation_node",
            id,
            AuditOutcomes.Success,
            detail: $"channel={resolvedChannel}; version={version}; name={node.Name}");

        return Ok(new
        {
            message = result.Message ?? $"Update {(requestedApply ? "trigger" : "notify")} delivered to Node {id}.",
            channel = resolvedChannel,
            availableVersion = version,
            requestedApply,
            nodeOnline = true
        });
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("nodes/{id}/pulse")]
    public async Task<IActionResult> PulseNode(string id, CancellationToken cancellationToken)
    {
        if (!ExecutionState.IsHub)
            return BadRequest("This endpoint is only available in Hub mode.");

        var node = _nodeRepo.GetById(id);
        if (node == null) return NotFound("Node not found.");

        var result = await _nodePulseService.TryPulseAsync(node, cancellationToken);
        if (!result.Success)
            return BadRequest(new { message = result.Detail ?? "Pulse failed." });

        return Ok(new { message = $"Wake-up pulse sent to Node {id}.", url = result.Url });
    }

    [HttpGet("settings")]
    public IActionResult GetSettings() => Ok(_settingsStore.Current);

    [Authorize(Roles = "Admin")]
    [HttpPatch("settings")]
    public async Task<IActionResult> UpdateSettings([FromBody] FederationSettings settings)
    {
        if (settings.Mode == ExecutionMode.Hub)
        {
            if (ExecutionState.IsPortable)
            {
                return BadRequest(new
                {
                    message = "Fleet Hub requires a production (background service) install. Portable evaluation supports Standalone/Node only."
                });
            }

            var licenseStatus = await _licenseService.GetCurrentStatusAsync();
            if (!HubModeGuard.IsHubBrainPresent() &&
                (!licenseStatus.IsActive || !licenseStatus.AllowsHub))
            {
                return BadRequest(new
                {
                    message = "Central Hub mode requires a Pro, Business, or Enterprise license."
                });
            }
        }

        HubModeGuard.EnforceHubMode(settings);
        _settingsStore.Save(settings);

        if (ExecutionState.IsHub && !string.IsNullOrWhiteSpace(settings.HubTailscaleMagicDns))
        {
            _hubCa.EnsureServerCertificateIncludesDnsNames([settings.HubTailscaleMagicDns]);
        }

        if (!ExecutionState.IsHub && settings.UseTailscaleForHubConnection)
        {
            ApplyTailscaleHubSyncRole();
        }

        _audit.Record(
            AuditActions.FederationSettingsUpdate,
            "federation",
            settings.Mode.ToString(),
            AuditOutcomes.Success,
            detail: $"mode={settings.Mode}");

        return Ok(new { message = "Federation settings updated. Please restart the appliance to apply changes." });
    }

    private void ApplyTailscaleHubSyncRole()
    {
        var networkSettings = _networkSettingsStore.Load();
        if (!TailscaleHubSyncConfigurator.ApplyToNetworkSettings(networkSettings, _networkInterfaceService, _logger))
            return;

        _networkSettingsStore.Save(networkSettings);
    }

    [HttpGet("tailscale/status")]
    public async Task<IActionResult> GetTailscaleStatus(CancellationToken cancellationToken)
    {
        var status = await _tailscaleStatusService.GetStatusAsync(cancellationToken);
        return Ok(status);
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("tailscale/test-reachability")]
    public async Task<IActionResult> TestTailscaleReachability(
        [FromBody] TailscaleReachabilityProbeRequest? request,
        CancellationToken cancellationToken)
    {
        var result = await _tailscaleReachabilityService.TestHubReachabilityAsync(request, cancellationToken);
        return Ok(result);
    }

    [HttpGet("tailscale/transport-summary")]
    public async Task<IActionResult> GetTransportSummary(CancellationToken cancellationToken)
    {
        var tailscaleStatus = await _tailscaleStatusService.GetStatusAsync(cancellationToken);

        NetworkInterfaceInfo? discovery = null;
        NetworkInterfaceInfo? federation = null;

        if (ExecutionState.IsHub)
        {
            discovery = null;
            federation = _networkInterfaceService.GetTailscaleInterface();
        }
        else
        {
            discovery = _networkInterfaceService
                .GetAllInterfaces(StacksAtlas.Core.Models.NetworkInterfaceScope.Discovery)
                .FirstOrDefault();
            federation = _networkInterfaceService.GetTailscaleInterface()
                ?? _networkInterfaceService
                    .GetAllInterfaces(StacksAtlas.Core.Models.NetworkInterfaceScope.Policy)
                    .FirstOrDefault(i => i.IsFederationTransport);
        }

        if (federation == null && !string.IsNullOrWhiteSpace(tailscaleStatus.TailnetIpv4))
        {
            federation = new NetworkInterfaceInfo(
                "tailscale-inferred",
                tailscaleStatus.Hostname ?? "tailscale0",
                "Tailscale (inferred)",
                tailscaleStatus.TailnetIpv4,
                "255.255.255.255",
                null,
                "Tunnel",
                0,
                tailscaleStatus.Connected ? "Up" : "Down",
                true);
        }

        return Ok(new
        {
            discoveryInterface = ExecutionState.IsHub
                ? "Hub mode (local discovery disabled)"
                : discovery == null ? null : $"{discovery.Name} ({discovery.IpAddress})",
            federationInterface = federation == null ? null : $"{federation.Name} ({federation.IpAddress})",
            useTailscaleForHubConnection = _settingsStore.Current.UseTailscaleForHubConnection,
            hubTailscaleMagicDns = _settingsStore.Current.HubTailscaleMagicDns,
            hubTailscaleIpv4 = _settingsStore.Current.HubTailscaleIpv4
        });
    }

    [HttpGet("nodes")]
    public IActionResult GetNodes()
    {
        if (!ExecutionState.IsHub)
            return BadRequest("This endpoint is only available in Hub mode.");

        var nodes = _nodeRepo.GetAll();
        var devices = _deviceRepo.GetAll(); // Hub mode IDeviceRepository.GetAll() returns all devices across all nodes

        var result = nodes.Select(n => {
            var nodeDevices = devices.Where(d => d.NodeId == n.Id).ToList();
            var signalRConnected = FederationSignalRRegistry.IsNodeConnected(n.Id, n.ConnectionId);
            var effectiveStatus = FederatedNodeStatus.ResolveEffectiveStatus(n, signalRConnected, _clock.UtcNow);
            return new {
                n.Id,
                n.Name,
                n.IPAddress,
                HttpPort = AppliancePortDefaults.ResolveHttpPortForNode(n.HttpPort, n.OS),
                HttpsPort = AppliancePortDefaults.ResolveHttpsPortForNode(n.HttpsPort),
                Status = effectiveStatus,
                n.LastSeenUtc,
                n.LastSyncUtc,
                n.Version,
                n.OS,
                n.Client,
                n.Building,
                n.Room,
                n.LicenseTier,
                n.IsPortable,
                n.DatabaseSize,
                n.SyncUsers,
                n.SyncUserRegistry,
                n.SyncSsoSettings,
                n.SyncAlertSettings,
                n.SyncSiemSettings,
                n.OverrideAlertSettings,
                n.OverrideSiemSettings,
                n.DelegateAlertDispatch,
                ScanSettingsJson = n.ScanSettingsJson,
                DeviceCount = nodeDevices.Count,
                StabilityScore = nodeDevices.Any() ? (int)nodeDevices.Average(d => d.StabilityScore) : 100,
                SecurityGrade = nodeDevices.Any() ? nodeDevices.Max(d => (int)d.SecurityGrade) : 0
            };
        });

        return Ok(result);
    }

    [Authorize(Roles = "Admin")]
    [HttpGet("nodes/enrollment-string")]
    public IActionResult GetEnrollmentString([FromQuery] bool tailscale = false)
    {
        if (!ExecutionState.IsHub)
            return BadRequest("Enrollment strings can only be generated in Hub mode.");

        var settings = _settingsStore.Current;
        var nodeId = Guid.NewGuid().ToString("N")[..8];
        var token = _tokenManager.GenerateToken(nodeId, TimeSpan.FromMinutes(15));

        var hubBaseUrl = HubEnrollmentEndpointResolver.ResolveHubBaseUrl(
            settings, Request.Scheme, Request.Host.Value, GetLocalIpAddress);
        var lanHost = HubEnrollmentEndpointResolver.ResolveLanHost(settings, hubBaseUrl);
        var tailscaleHost = HubEnrollmentEndpointResolver.ResolveTailscaleHost(settings);

        var host = tailscale && !string.IsNullOrWhiteSpace(tailscaleHost) ? tailscaleHost : lanHost;
        var enrollmentString = HubEnrollmentEndpointResolver.BuildEnrollmentConnectionString(host, token);

        string? tailscaleEnrollmentString = null;
        if (!string.IsNullOrWhiteSpace(tailscaleHost) &&
            !string.Equals(tailscaleHost, lanHost, StringComparison.OrdinalIgnoreCase))
        {
            tailscaleEnrollmentString = HubEnrollmentEndpointResolver.BuildEnrollmentConnectionString(tailscaleHost, token);
        }

        return Ok(new
        {
            enrollmentString,
            lanEnrollmentString = HubEnrollmentEndpointResolver.BuildEnrollmentConnectionString(lanHost, token),
            tailscaleEnrollmentString,
            preferredTransport = "lan"
        });
    }

    [Authorize(Roles = "Admin")]
    [HttpPatch("nodes/{id}")]
    public IActionResult UpdateNode(string id, [FromBody] FederatedNodeUpdateDto update)
    {
        if (!ExecutionState.IsHub)
            return BadRequest("This endpoint is only available in Hub mode.");

        if (id.ToLower() == "all")
        {
            var nodes = _nodeRepo.GetAll();
            foreach (var node in nodes)
            {
                if (update.ScanSettingsJson != null)
                {
                    node.ScanSettingsJson = ScanSettingsJsonHelper.PreserveNodeTelemetry(
                        node.ScanSettingsJson,
                        update.ScanSettingsJson);
                }
                _nodeRepo.UpsertNode(node);

                if (!string.IsNullOrEmpty(node.ConnectionId))
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var command = new FederationCommand
                            {
                                CommandType = "ConfigureScanning",
                                Parameters = new Dictionary<string, string> { { "ScanSettingsJson", node.ScanSettingsJson! } }
                            };
                            await _hubContext.Clients.Client(node.ConnectionId).SendAsync("ExecuteCommand", command);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Hub: Failed to push ConfigureScanning to Node {NodeId}", node.Id);
                        }
                    });
                }
            }
            _audit.Record(AuditActions.FederationNodeUpdate, "federation_node", "all", AuditOutcomes.Success, detail: "broadcast scan settings");
            return Ok(new { message = "Settings successfully applied and broadcasted to all nodes." });
        }

        var existing = _nodeRepo.GetById(id);
        if (existing == null) return NotFound("Node not found.");

        // Apply updates (only fields explicitly provided in PATCH body)
        if (update.Name != null) existing.Name = update.Name;
        if (update.Client != null) existing.Client = update.Client;
        if (update.Building != null) existing.Building = update.Building;
        if (update.Room != null) existing.Room = update.Room;

        var syncUsersChanged = false;
        if (update.SyncUsers.HasValue)
        {
            syncUsersChanged = existing.SyncUsers != update.SyncUsers.Value;
            existing.SyncUsers = update.SyncUsers.Value;
        }

        var syncUserRegistryChanged = false;
        if (update.SyncUserRegistry.HasValue)
        {
            syncUserRegistryChanged = existing.SyncUserRegistry != update.SyncUserRegistry.Value;
            existing.SyncUserRegistry = update.SyncUserRegistry.Value;
        }

        var syncSsoSettingsChanged = false;
        if (update.SyncSsoSettings.HasValue)
        {
            syncSsoSettingsChanged = existing.SyncSsoSettings != update.SyncSsoSettings.Value;
            existing.SyncSsoSettings = update.SyncSsoSettings.Value;
        }

        var syncAlertSettingsChanged = false;
        if (update.SyncAlertSettings.HasValue)
        {
            syncAlertSettingsChanged = existing.SyncAlertSettings != update.SyncAlertSettings.Value;
            existing.SyncAlertSettings = update.SyncAlertSettings.Value;
        }

        var syncSiemSettingsChanged = false;
        if (update.SyncSiemSettings.HasValue)
        {
            syncSiemSettingsChanged = existing.SyncSiemSettings != update.SyncSiemSettings.Value;
            existing.SyncSiemSettings = update.SyncSiemSettings.Value;
        }

        if (update.OverrideAlertSettings.HasValue)
            existing.OverrideAlertSettings = update.OverrideAlertSettings.Value;

        if (update.OverrideSiemSettings.HasValue)
            existing.OverrideSiemSettings = update.OverrideSiemSettings.Value;

        var delegateAlertDispatchChanged = false;
        if (update.DelegateAlertDispatch.HasValue)
        {
            delegateAlertDispatchChanged = existing.DelegateAlertDispatch != update.DelegateAlertDispatch.Value;
            existing.DelegateAlertDispatch = update.DelegateAlertDispatch.Value;
        }

        var scanSettingsChanged = false;
        if (update.ScanSettingsJson != null)
        {
            var merged = ScanSettingsJsonHelper.PreserveNodeTelemetry(
                existing.ScanSettingsJson,
                update.ScanSettingsJson);
            scanSettingsChanged = merged != existing.ScanSettingsJson;
            existing.ScanSettingsJson = merged;
        }

        _nodeRepo.UpsertNode(existing);

        if (!string.IsNullOrEmpty(existing.ConnectionId))
        {
            if (syncUserRegistryChanged)
            {
                var registryEnabled = existing.SyncUserRegistry;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var command = new FederationCommand
                        {
                            CommandType = "SyncUserRegistryGovernance",
                            Parameters = new Dictionary<string, string> { { "enabled", registryEnabled ? "true" : "false" } }
                        };
                        await _hubContext.Clients.Client(existing.ConnectionId).SendAsync("ExecuteCommand", command);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "SignalR: Failed to notify Node {NodeId} of registry governance state", existing.Id);
                    }
                });

                if (registryEnabled)
                {
                    if (!existing.IsIdentityImported)
                    {
                        _logger.LogInformation("FederationController: Registry governance enabled for Node {NodeId}. Requesting local user migration handshake...", existing.Id);
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                var command = new FederationCommand
                                {
                                    CommandType = "RequestUserImport"
                                };
                                await _hubContext.Clients.Client(existing.ConnectionId).SendAsync("ExecuteCommand", command);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "SignalR: Failed to request local user import from Node {NodeId}", existing.Id);
                            }
                        });
                    }
                    else
                    {
                        _ = Task.Run(async () => await _identitySync.PushIdentityStateToNodeAsync(existing.ConnectionId));
                    }
                }
                else
                {
                    existing.IsIdentityImported = false;
                    _nodeRepo.UpsertNode(existing);
                    
                    _ = Task.Run(async () => await _identitySync.PushIdentityStateToNodeAsync(existing.ConnectionId));
                }
            }

            if (syncSsoSettingsChanged)
            {
                var ssoEnabled = existing.SyncSsoSettings;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var command = new FederationCommand
                        {
                            CommandType = "SyncSsoSettingsGovernance",
                            Parameters = new Dictionary<string, string> { { "enabled", ssoEnabled ? "true" : "false" } }
                        };
                        await _hubContext.Clients.Client(existing.ConnectionId).SendAsync("ExecuteCommand", command);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "SignalR: Failed to notify Node {NodeId} of SSO governance state", existing.Id);
                    }
                });

                _ = Task.Run(async () => await _identitySync.PushIdentityStateToNodeAsync(existing.ConnectionId));
            }

            if (syncUsersChanged)
            {
                var legacyEnabled = existing.SyncUsers;
                existing.SyncUserRegistry = legacyEnabled;
                existing.SyncSsoSettings = legacyEnabled;
                _nodeRepo.UpsertNode(existing);

                _ = Task.Run(async () =>
                {
                    try
                    {
                        var command = new FederationCommand
                        {
                            CommandType = "SyncUserGovernance",
                            Parameters = new Dictionary<string, string> { { "enabled", legacyEnabled ? "true" : "false" } }
                        };
                        await _hubContext.Clients.Client(existing.ConnectionId).SendAsync("ExecuteCommand", command);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "SignalR: Failed to notify Node {NodeId} of legacy governance state", existing.Id);
                    }
                });

                if (legacyEnabled)
                {
                    if (!existing.IsIdentityImported)
                    {
                        _logger.LogInformation("FederationController: Legacy governance enabled for Node {NodeId}. Requesting local user migration...", existing.Id);
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                var command = new FederationCommand
                                {
                                    CommandType = "RequestUserImport"
                                };
                                await _hubContext.Clients.Client(existing.ConnectionId).SendAsync("ExecuteCommand", command);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "SignalR: Failed to request local user import from Node {NodeId}", existing.Id);
                            }
                        });
                    }
                    else
                    {
                        _ = Task.Run(async () => await _identitySync.PushIdentityStateToNodeAsync(existing.ConnectionId));
                    }
                }
                else
                {
                    existing.IsIdentityImported = false;
                    _nodeRepo.UpsertNode(existing);
                    _ = Task.Run(async () => await _identitySync.PushIdentityStateToNodeAsync(existing.ConnectionId));
                }
            }

            if (syncAlertSettingsChanged)
            {
                var alertEnabled = existing.SyncAlertSettings;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var command = new FederationCommand
                        {
                            CommandType = "SyncAlertGovernance",
                            Parameters = new Dictionary<string, string> { { "enabled", alertEnabled ? "true" : "false" } }
                        };
                        await _hubContext.Clients.Client(existing.ConnectionId).SendAsync("ExecuteCommand", command);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "SignalR: Failed to notify Node {NodeId} of alert governance state", existing.Id);
                    }
                });

                _ = Task.Run(async () => await _settingsSync.PushSettingsStateToNodeAsync(existing.ConnectionId));
            }

            if (syncSiemSettingsChanged)
            {
                var siemEnabled = existing.SyncSiemSettings;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var command = new FederationCommand
                        {
                            CommandType = "SyncSiemGovernance",
                            Parameters = new Dictionary<string, string> { { "enabled", siemEnabled ? "true" : "false" } }
                        };
                        await _hubContext.Clients.Client(existing.ConnectionId).SendAsync("ExecuteCommand", command);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "SignalR: Failed to notify Node {NodeId} of SIEM governance state", existing.Id);
                    }
                });

                _ = Task.Run(async () => await _settingsSync.PushSettingsStateToNodeAsync(existing.ConnectionId));
            }
        }

        if (scanSettingsChanged && !string.IsNullOrEmpty(existing.ConnectionId))
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var command = new FederationCommand
                    {
                        CommandType = "ConfigureScanning",
                        Parameters = new Dictionary<string, string> { { "ScanSettingsJson", existing.ScanSettingsJson! } }
                    };
                    await _hubContext.Clients.Client(existing.ConnectionId).SendAsync("ExecuteCommand", command);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Hub: Failed to push ConfigureScanning to Node {NodeId}", existing.Id);
                }
            });
        }

        _audit.Record(AuditActions.FederationNodeUpdate, "federation_node", id, AuditOutcomes.Success, detail: existing.Name);
        return Ok(existing);
    }

    public class FederatedNodeUpdateDto
    {
        public string? Name { get; set; }
        public string? Client { get; set; }
        public string? Building { get; set; }
        public string? Room { get; set; }
        public bool? SyncUsers { get; set; }
        public bool? SyncUserRegistry { get; set; }
        public bool? SyncSsoSettings { get; set; }
        public bool? SyncAlertSettings { get; set; }
        public bool? SyncSiemSettings { get; set; }
        public bool? OverrideAlertSettings { get; set; }
        public bool? OverrideSiemSettings { get; set; }
        public bool? DelegateAlertDispatch { get; set; }
        public string? ScanSettingsJson { get; set; }
    }

    [HttpGet("stats")]
    public IActionResult GetGlobalStats()
    {
        if (!ExecutionState.IsHub)
            return BadRequest("This endpoint is only available in Hub mode.");

        var nodes = _nodeRepo.GetAll();
        var totalDevices = _deviceRepo.GetCount();
        
        return Ok(new
        {
            TotalNodes = nodes.Count,
            OnlineNodes = nodes.Count(n => n.Status == "online"),
            TotalDevices = totalDevices,
            HubMode = true
        });
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("decouple")]
    public async Task<IActionResult> DecoupleNode()
    {
        if (ExecutionState.IsHub)
            return BadRequest("Decoupling is only available for standard Nodes.");

        try
        {
            _logger.LogWarning("Local request received to decouple appliance. Activating emergency break-glass procedure...");

            // 1. Try to notify Hub over SignalR connection if online
            try
            {
                var syncWorker = HttpContext.RequestServices.GetService<StacksAtlas.API.Workers.FederationSyncWorker>();
                if (syncWorker != null)
                {
                    await syncWorker.DecoupleFromHubAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Node: Failed to notify Hub of decoupling. Hub might be offline.");
            }

            // 2. Revert settings
            var settings = _settingsStore.Current;
            settings.HubUrl = null;
            settings.FederationToken = null;
            settings.SyncUsers = false;
            settings.SyncUserRegistry = false;
            settings.SyncSsoSettings = false;
            _settingsStore.Save(settings);

            // 3. Promote federated users to local
            var userCol = _db.GetCollection<User>("users");
            var users = userCol.FindAll().ToList();
            int promotedCount = 0;

            foreach (var u in users)
            {
                bool modified = false;
                if (u.OriginNodeId != null)
                {
                    u.OriginNodeId = null;
                    modified = true;
                }
                if (u.Provider != "Local")
                {
                    u.Provider = "Local";
                    modified = true;
                }

                if (modified)
                {
                    userCol.Update(u);
                    promotedCount++;
                }
            }

            _logger.LogInformation("Appliance successfully decoupled from Hub. Promoted {Count} federated users to local accounts.", promotedCount);

            // Schedule exit after response is sent so container restarts/reboots into stand-alone mode
            _ = Task.Run(async () =>
            {
                await Task.Delay(1500);
                _logger.LogInformation("Decoupling complete. Rebooting engine to re-bootstrap into Standalone mode...");
                Environment.Exit(0);
            });

            _audit.Record(AuditActions.FederationDecouple, "federation", null, AuditOutcomes.Success, detail: $"promotedUsers={promotedCount}");
            return Ok(new { message = "Appliance decoupled successfully." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decouple appliance.");
            return StatusCode(500, $"Decoupling failed: {ex.Message}");
        }
    }

    public class EnrollFromHubRequest
    {
        public string HubUrl { get; set; } = string.Empty;
        public string? FederationToken { get; set; }
        public string NodeId { get; set; } = string.Empty;
        public string? Client { get; set; }
        public string? Building { get; set; }
        public string? Room { get; set; }
        public string? HubRootCertificateBase64 { get; set; }
        public bool UseTailscaleForHubConnection { get; set; }
        public string? HubTailscaleMagicDns { get; set; }
        public string? HubTailscaleIpv4 { get; set; }
    }

    public class HubEnrollmentRequest
    {
        public string NodeUrl { get; set; } = string.Empty;
        public string NodeAdminPassword { get; set; } = string.Empty;
        public string NodeId { get; set; } = string.Empty;
        public string NodeName { get; set; } = string.Empty;
        public string? Client { get; set; }
        public string? Building { get; set; }
        public string? Room { get; set; }
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("nodes/{id}/reset-site")]
    public async Task<IActionResult> ResetSite(string id, CancellationToken cancellationToken)
    {
        if (!ExecutionState.IsHub)
            return BadRequest(new { message = "This endpoint is only available in Hub mode." });

        var existing = _nodeRepo.GetById(id);
        if (existing == null)
            return NotFound(new { message = "Node not found." });

        if (string.IsNullOrEmpty(existing.ConnectionId))
        {
            return Conflict(new
            {
                message = $"Node \"{existing.Name ?? id}\" is offline. Site reset requires an active connection."
            });
        }

        if (string.Equals(existing.Status, "resetting", StringComparison.OrdinalIgnoreCase))
        {
            return Conflict(new { message = "A site reset is already in progress for this node." });
        }

        var connectionId = existing.ConnectionId;
        var nodeName = existing.Name;
        var initiatedBy = User.Identity?.Name ?? "Hub Admin";
        var initiatedAtUtc = _clock.UtcNow;
        var hubSettings = _systemSettingsStore.Load();

        existing.Status = "resetting";
        _nodeRepo.UpsertNode(existing);

        var command = new FederationCommand
        {
            CommandType = "TriggerSiteReset",
            TargetId = id,
            Parameters = new Dictionary<string, string>
            {
                ["initiatedBy"] = initiatedBy,
                ["initiatedAtUtc"] = initiatedAtUtc.ToString("O"),
                ["hubDisplayName"] = hubSettings.Onboarding.SiteName ?? "Hub"
            }
        };

        var resetAccepted = false;
        string? nodeMessage = null;

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(60));

            var result = await _hubContext.Clients.Client(connectionId!)
                .InvokeAsync<FederationCommandResult>("ExecuteCommand", command, timeout.Token);

            resetAccepted = result?.Success == true;
            nodeMessage = result?.Message;
        }
        catch (Exception ex) when (SiteResetGovernance.IsHubSignalRDisconnectDuringReset(ex))
        {
            // Node stages wipe and disconnects/restarts before InvokeAsync completes  -  treat as success.
            _logger.LogInformation(
                ex,
                "Hub: Node {NodeId} disconnected during site reset (expected). Completing Hub fleet cleanup.",
                id);
            resetAccepted = true;
            nodeMessage = "Site reset staged; node disconnected for restart.";
        }

        if (!resetAccepted)
        {
            var node = _nodeRepo.GetById(id);
            if (node != null)
            {
                node.Status = !string.IsNullOrEmpty(node.ConnectionId) ? "online" : "offline";
                _nodeRepo.UpsertNode(node);
            }

            return BadRequest(new { message = nodeMessage ?? "Node rejected site reset command." });
        }

        try
        {
            var purge = await _telemetryPurge.PurgeAsync(id, nodeName, cancellationToken);

            _alertEventRepo.LogAlert(new AlertEvent
            {
                Id = Guid.NewGuid().ToString(),
                TriggeredAt = initiatedAtUtc,
                DeviceId = id,
                DeviceName = nodeName ?? id,
                DeviceIp = NetworkHelper.NormalizeIpAddress(existing.IPAddress),
                NodeId = id,
                NodeName = nodeName ?? id,
                Client = existing.Client,
                Building = existing.Building,
                Room = existing.Room,
                AlertType = AlertEventType.SiteResetInitiated,
                Success = true,
                ErrorMessage = $"Remote site reset by {initiatedBy}. Purged {purge.TotalRemoved} fleet records."
            });

            var updated = _nodeRepo.GetById(id) ?? existing;
            updated.Status = "offline";
            updated.ConnectionId = null;
            updated.LastSyncUtc = initiatedAtUtc;
            _nodeRepo.UpsertNode(updated);

            _logger.LogInformation(
                "Hub: Site reset completed for Node {NodeId}. Purged {PurgedCount} fleet records.",
                id,
                purge.TotalRemoved);

            _audit.Record(
                AuditActions.FederationSiteReset,
                "federation_node",
                id,
                AuditOutcomes.Success,
                detail: $"name={nodeName}; purged={purge.TotalRemoved}");

            return Ok(new
            {
                message = $"Site \"{nodeName ?? id}\" reset initiated. The node will reconnect automatically when it restarts.",
                nodeId = id,
                purgedRecords = purge.TotalRemoved
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hub: Site reset fleet cleanup failed for Node {NodeId}", id);
            return BadRequest(new { message = $"Site reset started on the node but Hub cleanup failed: {ex.Message}" });
        }
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("nodes/{id}")]
    public async Task<IActionResult> DeleteNode(string id)
    {
        if (!ExecutionState.IsHub)
            return BadRequest("This endpoint is only available in Hub mode.");

        var existing = _nodeRepo.GetById(id);
        if (existing == null) return NotFound("Node not found.");

        if (!string.IsNullOrEmpty(existing.ConnectionId))
        {
            try
            {
                var command = new FederationCommand
                {
                    CommandType = "Decouple",
                    TargetId = id
                };
                await _hubContext.Clients.Client(existing.ConnectionId).SendAsync("ExecuteCommand", command);
                _logger.LogInformation("Hub: Sent Decouple command to active node connection: {ConnectionId} for Node {NodeId}", existing.ConnectionId, id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Hub: Failed to send Decouple command to node {NodeId} over active connection.", id);
            }
        }

        _nodeRepo.Delete(id);

        if (_contextFactory != null)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            var devices = await context.Devices.Where(d => d.NodeId == id || (existing.Name != null && d.NodeId == existing.Name)).ToListAsync();
            if (devices.Count > 0)
            {
                context.Devices.RemoveRange(devices);
            }

            var logs = await context.FederatedLogs.Where(l => l.NodeId == id || (existing.Name != null && l.NodeId == existing.Name)).ToListAsync();
            if (logs.Count > 0)
            {
                context.FederatedLogs.RemoveRange(logs);
            }

            await context.SaveChangesAsync();
        }
        else
        {
            var pagedDevices = _deviceRepo.GetAll(true).Where(d => d.NodeId == id || (existing.Name != null && d.NodeId == existing.Name)).ToList();
            foreach (var d in pagedDevices)
            {
                _deviceRepo.HardDelete(d.Id);
            }
        }

        var userCol = _db.GetCollection<User>("users");
        var usersToDelete = userCol.Find(u => u.OriginNodeId == id).ToList();
        foreach (var u in usersToDelete)
        {
            userCol.Delete(u.Id);
        }

        _logger.LogInformation("Hub: Administrator manually removed Node {NodeId} and cleared associated telemetry.", id);
        _audit.Record(
            AuditActions.FederationNodeDelete,
            "federation_node",
            id,
            AuditOutcomes.Success,
            detail: existing.Name);
        return Ok(new { message = $"Node {id} removed successfully." });
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("enroll-from-hub")]
    public async Task<IActionResult> EnrollFromHub([FromBody] EnrollFromHubRequest request)
    {
        if (ExecutionState.IsHub)
            return BadRequest("Enrollment is only supported on standard Standalone/Node appliances.");

        // Fleet join is authorized by the Hub during enrollment; runtime entitlements inherit after first sync.
        if (string.IsNullOrWhiteSpace(request.HubUrl))
            return BadRequest("HubUrl is required.");

        var current = _settingsStore.Current;
        current.Mode = ExecutionMode.Standalone;
        current.HubUrl = request.HubUrl;
        current.FederationToken = string.IsNullOrWhiteSpace(request.FederationToken) ? null : request.FederationToken;
        current.NodeId = !string.IsNullOrWhiteSpace(request.NodeId) ? request.NodeId : Guid.NewGuid().ToString();
        current.Client = request.Client;
        current.Building = request.Building;
        current.Room = request.Room;
        current.UseTailscaleForHubConnection = request.UseTailscaleForHubConnection;
        current.HubTailscaleMagicDns = request.HubTailscaleMagicDns;
        current.HubTailscaleIpv4 = request.HubTailscaleIpv4;

        _settingsStore.Save(current);

        if (current.UseTailscaleForHubConnection)
        {
            ApplyTailscaleHubSyncRole();
        }

        if (!string.IsNullOrEmpty(request.HubRootCertificateBase64))
        {
            try
            {
                var certsDir = StacksAtlas.Core.Helpers.PlatformPaths.GetCertDirectory();
                var hubRootCrtPath = Path.Combine(certsDir, "hub_root.crt");
                Directory.CreateDirectory(certsDir);
                var certBytes = Convert.FromBase64String(request.HubRootCertificateBase64);
                System.IO.File.WriteAllBytes(hubRootCrtPath, certBytes);
                _logger.LogInformation("Node: Saved Hub Root CA certificate received from Hub.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Node: Failed to save Hub Root CA certificate during enrollment.");
            }
        }

        _logger.LogWarning("Node: Enrolled from Hub. Re-configuring appliance and scheduled restart in 1.5 seconds...");

        _ = Task.Run(async () =>
        {
            await Task.Delay(1500);
            Environment.Exit(0);
        });

        _audit.Record(AuditActions.FederationEnroll, "federation", request.NodeId, AuditOutcomes.Success, detail: $"hub={request.HubUrl}");
        return Ok(new { message = "Enrollment successful. Node is restarting to connect to Hub." });
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("nodes/enroll")]
    public async Task<IActionResult> EnrollNodeFromHub([FromBody] HubEnrollmentRequest request)
    {
        if (!ExecutionState.IsHub)
            return BadRequest("This endpoint is only available in Hub mode.");

        var hubLicense = await _licenseService.GetCurrentStatusAsync();
        if (!hubLicense.IsActive || !hubLicense.AllowsHub)
        {
            return BadRequest(new
            {
                message = "Hub enrollment requires a Pro, Business, or Enterprise license."
            });
        }

        if (string.IsNullOrWhiteSpace(request.NodeUrl) || string.IsNullOrWhiteSpace(request.NodeAdminPassword))
            return BadRequest("NodeUrl and NodeAdminPassword are required.");

        var cleanNodeUrl = request.NodeUrl.TrimEnd('/');
        
        try
        {
            var urlCandidates = BuildNodeUrlCandidates(cleanNodeUrl);
            if (urlCandidates.Count == 0)
                return BadRequest("Invalid Node URL.");

            var (nodeToken, workingUrl, loginError) = await TryNodeLoginAsync(urlCandidates, request.NodeAdminPassword);
            if (string.IsNullOrEmpty(nodeToken) || string.IsNullOrEmpty(workingUrl))
            {
                _logger.LogWarning("Hub: All Direct Push login attempts failed for {NodeUrl}. Last error: {Error}", cleanNodeUrl, loginError);
                return BadRequest($"Failed to authenticate against target Node. {loginError}");
            }

            cleanNodeUrl = workingUrl;
            _logger.LogInformation("Hub: Direct Push using reachable Node URL {NodeUrl}", cleanNodeUrl);

            var hubSettings = _settingsStore.Current;
            var hubUrl = HubEnrollmentEndpointResolver.ResolveDirectPushHubUrl(
                hubSettings, Request.Scheme, Request.Host.Value, GetLocalIpAddress);
            var federationToken = EnsureHubFederationToken();

            var nodeId = !string.IsNullOrWhiteSpace(request.NodeId) 
                ? request.NodeId 
                : (!string.IsNullOrWhiteSpace(request.NodeName) 
                    ? System.Text.RegularExpressions.Regex.Replace(request.NodeName.Trim().ToLowerInvariant(), @"[^a-z0-9\-_]+", "-").Trim('-')
                    : Guid.NewGuid().ToString());

            // Pre-register the node record in the Hub's database
            var existingNode = _nodeRepo.GetById(nodeId);

            _nodeRepo.UpsertNode(new FederatedNode
            {
                Id = nodeId,
                Name = !string.IsNullOrWhiteSpace(request.NodeName) ? request.NodeName : $"Node-{nodeId.Substring(0, 6)}",
                Status = "offline",
                IPAddress = new Uri(cleanNodeUrl).Host,
                LastSeenUtc = DateTime.MinValue,
                Client = request.Client,
                Building = request.Building,
                Room = request.Room,
                SyncUsers = false,
                SyncUserRegistry = false,
                SyncSsoSettings = false
            });

            string? rootCaBase64 = null;
            try
            {
                var rootCa = _hubCa.GetOrCreateRootCa();
                var rootCaBytes = rootCa.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Cert);
                rootCaBase64 = Convert.ToBase64String(rootCaBytes);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Hub: Failed to export Root CA certificate for Node enrollment.");
            }

            var enrollPayload = new
            {
                HubUrl = hubUrl,
                FederationToken = federationToken,
                NodeId = nodeId,
                Client = request.Client,
                Building = request.Building,
                Room = request.Room,
                HubRootCertificateBase64 = rootCaBase64,
                UseTailscaleForHubConnection = false,
                HubTailscaleMagicDns = (string?)null,
                HubTailscaleIpv4 = (string?)null
            };

            var enrollContent = new StringContent(
                System.Text.Json.JsonSerializer.Serialize(enrollPayload),
                System.Text.Encoding.UTF8,
                "application/json"
            );

            using var enrollClient = CreateEnrollmentHttpClient();
            enrollClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", nodeToken);
            
            _logger.LogInformation("Hub: Sending enrollment payload to target Node.");
            var enrollResponse = await enrollClient.PostAsync($"{cleanNodeUrl}/api/federation/enroll-from-hub", enrollContent);
            if (!enrollResponse.IsSuccessStatusCode)
            {
                var errBody = await enrollResponse.Content.ReadAsStringAsync();
                _logger.LogError("Hub: Node enrollment command failed. Status: {Status}, Response: {Response}", enrollResponse.StatusCode, errBody);
                _nodeRepo.Delete(nodeId); // Rollback
                return BadRequest($"Node enrollment failed: {enrollResponse.StatusCode}. Detail: {errBody}");
            }

            _logger.LogInformation("Hub: Node {NodeId} successfully commanded to enroll.", nodeId);
            _audit.Record(
                AuditActions.FederationEnroll,
                "federation_node",
                nodeId,
                AuditOutcomes.Success,
                detail: $"nodeUrl={cleanNodeUrl}");
            return Ok(new { nodeId, message = "Enrollment command accepted by the remote Node. It will reboot and connect shortly." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hub: Failed to enroll remote Node at {NodeUrl}", cleanNodeUrl);
            return StatusCode(500, $"An error occurred during enrollment: {ex.Message}");
        }
    }

    [AllowAnonymous]
    [HttpPost("enroll")]
    public async Task<IActionResult> Enroll([FromBody] NodeEnrollmentClient.EnrollRequestDto request)
    {
        if (!ExecutionState.IsHub)
            return BadRequest("Enrollment is only supported in Hub mode.");

        var hubLicense = await _licenseService.GetCurrentStatusAsync();
        if (!hubLicense.IsActive || !hubLicense.AllowsHub)
        {
            return BadRequest(new
            {
                message = "Hub enrollment requires a Pro, Business, or Enterprise license."
            });
        }

        if (string.IsNullOrWhiteSpace(request.NodeId) ||
            string.IsNullOrWhiteSpace(request.Token) || 
            string.IsNullOrWhiteSpace(request.Csr) || 
            string.IsNullOrWhiteSpace(request.Signature))
        {
            return BadRequest("Missing required enrollment parameters.");
        }

        // 1. Verify standard enrollment token signature & expiry
        if (!_tokenManager.TryValidateToken(request.Token, out var validatedNodeId) || validatedNodeId != request.NodeId)
        {
            return Unauthorized("Invalid or expired enrollment token.");
        }

        try
        {
            // 2. Parse CSR to retrieve client public key
            byte[] csrBytes = System.Text.Encoding.UTF8.GetBytes(request.Csr);
            
            // Extract DER bytes from CSR PEM
            var pemText = request.Csr;
            var lines = pemText.Split('\n');
            var sb = new System.Text.StringBuilder();
            bool inside = false;
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("-----BEGIN CERTIFICATE REQUEST")) { inside = true; continue; }
                if (trimmed.StartsWith("-----END CERTIFICATE REQUEST")) { inside = false; break; }
                if (inside) sb.Append(trimmed);
            }
            var derBytes = Convert.FromBase64String(sb.ToString());
            
            var csr = System.Security.Cryptography.X509Certificates.CertificateRequest.LoadSigningRequest(
                derBytes, 
                System.Security.Cryptography.HashAlgorithmName.SHA256, 
                System.Security.Cryptography.X509Certificates.CertificateRequestLoadOptions.Default);

            using var ecdsa = csr.PublicKey.GetECDsaPublicKey();
            if (ecdsa == null)
            {
                return BadRequest("mTLS enrollment requires ECDsa public key.");
            }

            // 3. Cryptographically verify token proof-of-possession signature
            var tokenBytes = Convert.FromBase64String(request.Token);
            var signatureBytes = Convert.FromBase64String(request.Signature);
            if (!ecdsa.VerifyData(tokenBytes, signatureBytes, System.Security.Cryptography.HashAlgorithmName.SHA256))
            {
                // Verify using alternate signature styles or raw parameters if needed, but VerifyData with SHA256 is correct
                return Unauthorized("Proof-of-possession verification failed.");
            }

            // 4. Hardware identity reconciliation (Phase D) before per-NodeId checks
            var reconcile = await _identityReconciliation.TryReconcileRegistrationAsync(
                request.NodeId,
                request.HardwareId,
                "Hub Enroll",
                CancellationToken.None);

            if (!reconcile.Success)
                return BadRequest(reconcile.Message);

            // 5. Check Node Limit and Hardware ID mismatch before enrolling
            var existingNode = _nodeRepo.GetById(request.NodeId);
            if (existingNode != null && !string.IsNullOrEmpty(existingNode.HardwareId) && 
                !string.IsNullOrEmpty(request.HardwareId) && existingNode.HardwareId != request.HardwareId)
            {
                _logger.LogWarning("Hub CA: Node enrollment rejected for {NodeId}. Hardware ID mismatch (Existing: {Existing}, Requested: {Requested})", 
                    request.NodeId, existingNode.HardwareId, request.HardwareId);
                return BadRequest("Node enrollment rejected due to hardware identifier mismatch.");
            }

            var clientCert = _hubCa.SignNodeClientCertificate(csrBytes, request.NodeId, TimeSpan.FromDays(180));
            var rootCa = _hubCa.GetOrCreateRootCa();

            // 5. Upsert Node record in the database
            var nodeToSave = existingNode ?? new FederatedNode { Id = request.NodeId };
            
            nodeToSave.Name = !string.IsNullOrWhiteSpace(request.FriendlyName) ? request.FriendlyName : $"Node-{request.NodeId.Substring(0, 6)}";
            nodeToSave.CertificateSerialNumber = clientCert.SerialNumber;
            nodeToSave.IsRevoked = false;
            nodeToSave.Client = request.Client;
            nodeToSave.Building = request.Building;
            nodeToSave.Room = request.Room;
            nodeToSave.Status = "offline";
            nodeToSave.LastSeenUtc = DateTime.UtcNow;
            if (!string.IsNullOrEmpty(request.HardwareId))
            {
                nodeToSave.HardwareId = request.HardwareId;
            }

            _nodeRepo.UpsertNode(nodeToSave);

            // 6. Return response with base64 certs
            var clientCertBase64 = Convert.ToBase64String(clientCert.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Cert));
            var rootCaBase64 = Convert.ToBase64String(rootCa.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Cert));

            _logger.LogInformation("Hub CA: Node {NodeId} successfully enrolled via mTLS.", request.NodeId);

            return Ok(new NodeEnrollmentClient.EnrollResponseDto
            {
                ClientCertificateBase64 = clientCertBase64,
                HubRootCertificateBase64 = rootCaBase64
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hub CA: Failed to complete mTLS enrollment for Node {NodeId}", request.NodeId);
            return StatusCode(500, $"Internal error during enrollment: {ex.Message}");
        }
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("enroll-node-mtls")]
    public async Task<IActionResult> EnrollNodeMtls([FromBody] LocalEnrollRequest request)
    {
        if (ExecutionState.IsHub)
            return BadRequest("This endpoint is only available on Standalone/Node appliances.");

        if (string.IsNullOrWhiteSpace(request.EnrollmentString))
            return BadRequest("Enrollment string is required.");

        var enrollmentClient = HttpContext.RequestServices.GetRequiredService<NodeEnrollmentClient>();
        
        _logger.LogInformation("Node: Initiating mTLS enrollment handshake...");
        
        var enrollResult = await enrollmentClient.EnrollAsync(
            request.EnrollmentString, 
            request.FriendlyName, 
            request.Client, 
            request.Building, 
            request.Room);

        if (!enrollResult.Success)
        {
            return BadRequest(enrollResult.ErrorMessage ?? "Enrollment handshake failed. Check system logs for details.");
        }

        var currentSettings = _settingsStore.Current;
        
        try
        {
            var uri = new Uri(request.EnrollmentString);
            var query = uri.Query;
            if (query.StartsWith("?")) query = query[1..];
            string token = string.Empty;
            foreach (var part in query.Split('&'))
            {
                var kv = part.Split('=');
                if (kv.Length == 2 && kv[0].Equals("token", StringComparison.OrdinalIgnoreCase))
                {
                    token = Uri.UnescapeDataString(kv[1]);
                    break;
                }
            }
            if (!string.IsNullOrEmpty(token))
            {
                var decodedBytes = Convert.FromBase64String(token);
                var decodedString = System.Text.Encoding.UTF8.GetString(decodedBytes);
                var tokenParts = decodedString.Split(':');
                currentSettings.NodeId = tokenParts[0];
            }
        }
        catch { /* Fallback */ }

        var parsedUri = new Uri(request.EnrollmentString);
        currentSettings.HubUrl = $"https://{parsedUri.Host}:5002";
        currentSettings.FederationToken = null; 
        currentSettings.Client = request.Client;
        currentSettings.Building = request.Building;
        currentSettings.Room = request.Room;
        currentSettings.NodeDisplayName = request.FriendlyName;

        var enrollHost = parsedUri.Host;
        if (enrollHost.Contains(".ts.net", StringComparison.OrdinalIgnoreCase))
        {
            currentSettings.UseTailscaleForHubConnection = true;
            currentSettings.HubTailscaleMagicDns = enrollHost;
        }
        else if (enrollHost.StartsWith("100.", StringComparison.Ordinal))
        {
            currentSettings.UseTailscaleForHubConnection = true;
            currentSettings.HubTailscaleIpv4 = enrollHost;
        }

        _settingsStore.Save(currentSettings);

        _logger.LogWarning("Node: Enrollment complete. Restarting system in 1.5 seconds to establish secure mTLS sync...");

        _ = Task.Run(async () =>
        {
            await Task.Delay(1500);
            Environment.Exit(0);
        });

        _audit.Record(AuditActions.FederationEnroll, "federation", request.FriendlyName, AuditOutcomes.Success, detail: "mtls");
        return Ok(new { message = "Enrollment successful. Node is restarting to establish secure mTLS channel." });
    }

    public class LocalEnrollRequest
    {
        public string EnrollmentString { get; set; } = string.Empty;
        public string FriendlyName { get; set; } = string.Empty;
        public string? Client { get; set; }
        public string? Building { get; set; }
        public string? Room { get; set; }
    }

    public static List<string> BuildNodeUrlCandidates(string rawUrl)
    {
        var results = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var trimmed = rawUrl.Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(trimmed))
            return results;

        if (!trimmed.Contains("://", StringComparison.Ordinal))
            trimmed = "http://" + trimmed;

        void Add(string url)
        {
            url = url.TrimEnd('/');
            if (!string.IsNullOrWhiteSpace(url) && seen.Add(url))
                results.Add(url);
        }

        Add(trimmed);

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            var host = uri.Host;
            var isHttps = uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
            var isHttp = uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);

            if (isHttps && (uri.IsDefaultPort || uri.Port == 443))
            {
                Add($"http://{host}:5000");
                Add($"https://{host}:5001");
            }

            if (isHttp && (uri.IsDefaultPort || uri.Port == 80))
                Add($"http://{host}:5000");
        }

        return results;
    }

    private static HttpClient CreateEnrollmentHttpClient() =>
        new(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        })
        { Timeout = TimeSpan.FromSeconds(15) };

    private async Task<(string? Token, string? WorkingUrl, string? Error)> TryNodeLoginAsync(
        IEnumerable<string> candidates,
        string password)
    {
        var loginJson = System.Text.Json.JsonSerializer.Serialize(new { Username = "admin", Password = password });
        string? lastError = null;

        foreach (var url in candidates)
        {
            try
            {
                using var client = CreateEnrollmentHttpClient();
                _logger.LogInformation("Hub: Attempting to authenticate against target Node at {NodeUrl}", url);

                var loginContent = new StringContent(loginJson, System.Text.Encoding.UTF8, "application/json");
                var loginResponse = await client.PostAsync($"{url}/api/auth/login", loginContent);

                if (!loginResponse.IsSuccessStatusCode)
                {
                    var errBody = await loginResponse.Content.ReadAsStringAsync();
                    lastError = $"{url}: HTTP {(int)loginResponse.StatusCode}";
                    _logger.LogWarning("Hub: Authentication failed at {NodeUrl}. Status: {Status}, Response: {Response}",
                        url, loginResponse.StatusCode, errBody);
                    continue;
                }

                var loginResultJson = await loginResponse.Content.ReadAsStringAsync();
                using var loginDoc = System.Text.Json.JsonDocument.Parse(loginResultJson);
                if (!loginDoc.RootElement.TryGetProperty("token", out var tokenProp) &&
                    !loginDoc.RootElement.TryGetProperty("Token", out tokenProp))
                {
                    lastError = $"{url}: login succeeded but no token in response";
                    continue;
                }

                return (tokenProp.GetString(), url, null);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                lastError = $"{url}: {ex.Message}";
                _logger.LogWarning(ex, "Hub: Connection to {NodeUrl} failed, trying next candidate.", url);
            }
        }

        return (null, null, lastError ?? "All connection attempts failed.");
    }

    private string EnsureHubFederationToken()
    {
        var settings = _settingsStore.Current;
        if (!string.IsNullOrWhiteSpace(settings.FederationToken))
            return settings.FederationToken;

        var token = Guid.NewGuid().ToString("N");
        settings.FederationToken = token;
        _settingsStore.Save(settings);
        _logger.LogInformation("Hub: Generated FederationToken for Direct Push enrollment (was unset after Fresh Start or first Hub bootstrap).");
        return token;
    }

    private string GetLocalIpAddress()
    {
        try
        {
            var interfaces = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up && 
                             ni.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                .OrderByDescending(ni => ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Ethernet || 
                                         ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211)
                .ThenBy(ni => ni.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase) || 
                              ni.Description.Contains("WSL", StringComparison.OrdinalIgnoreCase) || 
                              ni.Description.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase) || 
                              ni.Description.Contains("vEthernet", StringComparison.OrdinalIgnoreCase) || 
                              ni.Description.Contains("VirtualBox", StringComparison.OrdinalIgnoreCase) || 
                              ni.Description.Contains("VMware", StringComparison.OrdinalIgnoreCase));

            foreach (var ni in interfaces)
            {
                var ip = ni.GetIPProperties().UnicastAddresses
                    .Where(ua => ua.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && 
                                 !ua.Address.ToString().StartsWith("169.254.") && 
                                 !ua.Address.ToString().StartsWith("127."))
                    .Select(ua => ua.Address.ToString())
                    .FirstOrDefault();

                if (ip != null) return ip;
            }

            return "localhost";
        }
        catch
        {
            return "localhost";
        }
    }

    [Authorize]
    [HttpGet("backlog-status")]
    public IActionResult GetBacklogStatus([FromServices] IFederationBacklogService backlogService)
    {
        if (ExecutionState.IsHub)
            return Ok(new { message = "Backlog status is only available on federated Nodes." });

        var syncWorker = HttpContext.RequestServices.GetService<FederationSyncWorker>();
        if (syncWorker != null)
            return Ok(syncWorker.GetBacklogStatus());

        if (ExecutionState.IsFederationActive)
        {
            syncWorker = HttpContext.RequestServices.GetRequiredService<FederationSyncWorker>();
            return Ok(syncWorker.GetBacklogStatus());
        }

        return Ok(backlogService.GetStatus(hubConnected: false, deepSleep: false));
    }

    [AllowAnonymous]
    [HttpPost("pulse")]
    public IActionResult ReceiveHubPulse()
    {
        if (ExecutionState.IsHub)
            return BadRequest("This endpoint is only active in Node mode.");

        var expectedToken = _settingsStore.Current.FederationToken;
        if (string.IsNullOrWhiteSpace(expectedToken))
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "Federation token not configured on this node." });

        if (!Request.Headers.TryGetValue(FederationPulseAuth.HeaderName, out var provided)
            || provided.Count == 0
            || !string.Equals(provided[0], expectedToken, StringComparison.Ordinal))
        {
            return Unauthorized(new { message = "Invalid federation pulse token." });
        }

        var syncWorker = HttpContext.RequestServices.GetService<StacksAtlas.API.Workers.FederationSyncWorker>();
        if (syncWorker == null)
            return BadRequest("FederationSyncWorker is not registered/active on this Node.");

        syncWorker.WakeUp();
        return Ok(new { message = "Wake-up pulse accepted. Reconnection initiated." });
    }
}
