using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Data;
using StacksAtlas.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using StacksAtlas.API.Services;
using LiteDB;
using StacksAtlas.Core.Services.Licensing;
using StacksAtlas.Core.Services.Security;
using StacksAtlas.Core.Helpers;
using StacksAtlas.Core.Services.Governance;
using StacksAtlas.Core.Services.Federation;

namespace StacksAtlas.API.Hubs;

/// <summary>
/// The primary SignalR Hub for the StacksAtlas Federation Protocol.
/// Nodes connect to this Hub to establish a real-time, two-way sync pipeline.
/// </summary>
public class FederationHub : Hub
{
    private readonly ILogger<FederationHub> _logger;
    private readonly IDeviceRepository _deviceRepo;
    private readonly IFederatedNodeRepository _nodeRepo;
    private readonly FederationSettingsStore _settings;
    private readonly IFederationIdentitySyncService _identitySync;
    private readonly IFederationSettingsSyncService _settingsSync;
    private readonly IHubContext<FederationHub> _hubContext;
    private readonly LiteDatabase _db;
    private readonly Microsoft.EntityFrameworkCore.IDbContextFactory<StacksAtlas.Core.Data.Hub.HubDbContext>? _contextFactory;
    private readonly FederatedIngestionBuffer _ingestionBuffer;
    private readonly StacksAtlas.Core.Database.IAlertEventRepository _alertEventRepo;

    private readonly ILicenseService _licenseService;
    private readonly IHubCertificateAuthority _hubCa;
    private readonly INodeIdentityReconciliationService _identityReconciliation;
    private readonly RemoteExecutionService? _remoteExecution;

    // Track active nodes: ConnectionId -> NodeId (see FederationSignalRRegistry)

    public FederationHub(
        ILogger<FederationHub> logger, 
        IDeviceRepository deviceRepo, 
        IFederatedNodeRepository nodeRepo,
        FederationSettingsStore settings,
        IFederationIdentitySyncService identitySync,
        IFederationSettingsSyncService settingsSync,
        IHubContext<FederationHub> hubContext,
        LiteDatabase db,
        StacksAtlas.Core.Database.IAlertEventRepository alertEventRepo,
        ILicenseService licenseService,
        IHubCertificateAuthority hubCa,
        INodeIdentityReconciliationService identityReconciliation,
        RemoteExecutionService? remoteExecution = null,
        Microsoft.EntityFrameworkCore.IDbContextFactory<StacksAtlas.Core.Data.Hub.HubDbContext>? contextFactory = null,
        FederatedIngestionBuffer? ingestionBuffer = null)
    {
        _logger = logger;
        _deviceRepo = deviceRepo;
        _nodeRepo = nodeRepo;
        _settings = settings;
        _identitySync = identitySync;
        _settingsSync = settingsSync;
        _hubContext = hubContext;
        _db = db;
        _alertEventRepo = alertEventRepo;
        _licenseService = licenseService;
        _hubCa = hubCa;
        _identityReconciliation = identityReconciliation;
        _remoteExecution = remoteExecution;
        _contextFactory = contextFactory;
        _ingestionBuffer = ingestionBuffer ?? new FederatedIngestionBuffer();
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("SignalR: Node connected {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (FederationSignalRRegistry.TryUnregister(Context.ConnectionId, out var nodeId)
            && !string.IsNullOrEmpty(nodeId))
        {
            MarkNodeOffline(nodeId);
            _logger.LogInformation("SignalR: Node {NodeId} disconnected  -  marked offline.", nodeId);
        }
        else
        {
            // Evicted or hard drop: still clear DB row if this connection id was registered.
            var node = _nodeRepo.GetAll()
                .FirstOrDefault(n => string.Equals(n.ConnectionId, Context.ConnectionId, StringComparison.Ordinal));
            if (node != null)
            {
                MarkNodeOffline(node.Id);
                _logger.LogInformation(
                    "SignalR: Stale connection {ConnectionId} for Node {NodeId} closed  -  marked offline.",
                    Context.ConnectionId,
                    node.Id);
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    private void MarkNodeOffline(string nodeId)
    {
        var node = _nodeRepo.GetById(nodeId);
        if (node == null)
            return;

        node.Status = FederatedNodeStatus.Offline;
        node.ConnectionId = null;
        _nodeRepo.UpsertNode(node);
    }

    internal static void EvictActiveConnectionsForNode(string nodeId) =>
        FederationSignalRRegistry.EvictConnectionsForNode(nodeId);

    /// <summary>
    /// Called by the Node immediately after connecting to authenticate and register its ID.
    /// </summary>
    public async Task<FederationRegistrationResponse> AuthenticateNode(FederationRegistrationRequest request)
    {
        try
        {
            // 1. Validate federationToken against Hub settings (skip if mTLS authenticated)
            var clientCert = Context.GetHttpContext()?.Connection.ClientCertificate;
            bool isMtlsAuthenticated = clientCert != null;

            var hubToken = _settings.Current.FederationToken;
            if (!isMtlsAuthenticated)
            {
                if (string.IsNullOrEmpty(hubToken))
                {
                    _logger.LogWarning("SignalR: Authentication failed for Node {NodeId}. Hub FederationToken is not configured.", request.NodeId);
                    return new FederationRegistrationResponse { Success = false, Message = "Hub federation token is not configured." };
                }

                if (request.FederationToken != hubToken)
                {
                    _logger.LogWarning("SignalR: Authentication failed for Node {NodeId}. Invalid Token.", request.NodeId);
                    return new FederationRegistrationResponse { Success = false, Message = "Invalid Federation Token." };
                }
            }

            FederationSignalRRegistry.EvictConnectionsForNode(request.NodeId, Context.ConnectionId);

            var existingNode = _nodeRepo.GetById(request.NodeId);
            if (existingNode == null)
            {
                _logger.LogWarning("SignalR: Authentication rejected for Node {NodeId}. Node has been deleted or is not enrolled.", request.NodeId);
                return new FederationRegistrationResponse 
                { 
                    Success = false, 
                    Message = "This Node is not registered or has been deleted from the Hub.",
                    Decouple = true 
                };
            }

            var reconcile = await _identityReconciliation.TryReconcileRegistrationAsync(
                request.NodeId,
                request.HardwareId,
                "SignalR Register",
                Context.ConnectionAborted);

            if (!reconcile.Success)
            {
                return new FederationRegistrationResponse
                {
                    Success = false,
                    Message = reconcile.Message ?? "Node identity reconciliation failed."
                };
            }

            if (reconcile.Reconciled && !string.IsNullOrEmpty(reconcile.RetiredNodeId))
                EvictActiveConnectionsForNode(reconcile.RetiredNodeId);

            existingNode = _nodeRepo.GetById(request.NodeId) ?? existingNode;

            // Validate Hardware ID to prevent spoofing
            if (existingNode != null && !string.IsNullOrEmpty(existingNode.HardwareId) && 
                !string.IsNullOrEmpty(request.HardwareId) && existingNode.HardwareId != request.HardwareId)
            {
                _logger.LogWarning("SignalR: Authentication rejected for Node {NodeId}. Hardware ID mismatch (Existing: {Existing}, Requested: {Requested})", 
                    request.NodeId, existingNode.HardwareId, request.HardwareId);
                return new FederationRegistrationResponse 
                { 
                    Success = false, 
                    Message = "Authentication rejected due to hardware identifier mismatch." 
                };
            }

            // Adopt Hardware ID on first connect if empty (legacy upgrade)
            string? targetHwid = existingNode?.HardwareId;
            if (existingNode != null && string.IsNullOrEmpty(existingNode.HardwareId) && !string.IsNullOrEmpty(request.HardwareId))
            {
                targetHwid = request.HardwareId;
                _logger.LogInformation("SignalR: Associating Hardware ID {HardwareId} with registered Node {NodeId} on first handshake.", request.HardwareId, request.NodeId);
            }

            // Verify node limit and fleet entitlement
            var licenseStatus = await _licenseService.GetCurrentStatusAsync();
            if (!licenseStatus.IsActive || !licenseStatus.AllowsHub)
            {
                _logger.LogWarning(
                    "SignalR: Authentication rejected for Node {NodeId}. Hub license does not allow federation (Tier: {Tier}, Active: {Active}).",
                    request.NodeId,
                    licenseStatus.Tier,
                    licenseStatus.IsActive);
                return new FederationRegistrationResponse
                {
                    Success = false,
                    Message = "Hub license does not include fleet enrollment. Upgrade to Pro or higher."
                };
            }


            if (!IsPaidFleetNodeTier(request.LicenseTier))
            {
                if (request.IsPortable)
                {
                    _logger.LogInformation(
                        "SignalR: Portable Node {NodeId} enrolled (Free/Home entitlements). Soft-allow for soft launch; activate Business on an installed appliance for paid fleet sites.",
                        request.NodeId);
                }
                else
                {
                    _logger.LogInformation(
                        "SignalR: Node {NodeId} connected with unlicensed site tier ({Tier}). Federation soft-allowed; activate Business on the remote appliance (or confirm the site is not portable).",
                        request.NodeId,
                        request.LicenseTier ?? "unknown");
                }
            }

            _logger.LogInformation("SignalR: Authenticating Node {NodeId} (Connection: {ConnectionId})",
                request.NodeId,
                Context.ConnectionId);
            
            FederationSignalRRegistry.Register(Context.ConnectionId, request.NodeId);
            
            // Upsert Node info to database
            var remoteIp = NetworkHelper.NormalizeIpAddress(
                Context.GetHttpContext()?.Connection.RemoteIpAddress?.ToString() ?? "unknown");
            
            var lastSync = existingNode?.LastSyncUtc;

            // Self-healing settings reconciliation
            var reconciledScanSettings = ScanSettingsJsonHelper.ReconcileScanSettings(
                existingNode?.ScanSettingsJson,
                request.ScanSettingsJson);

            var syncUsers = existingNode?.SyncUsers ?? false;
            var syncUserRegistry = existingNode?.SyncUserRegistry ?? false;
            var syncSsoSettings = existingNode?.SyncSsoSettings ?? false;
            var syncAlertSettings = existingNode?.SyncAlertSettings ?? false;
            var syncSiemSettings = existingNode?.SyncSiemSettings ?? false;
            var isIdentityImported = existingNode?.IsIdentityImported ?? false;

            if ((syncUsers || syncUserRegistry) && !isIdentityImported && request.LocalUsersToImport != null && request.LocalUsersToImport.Count > 0)
            {
                _logger.LogInformation("SignalR: Node {NodeId} registering with active SyncUserRegistry and pending import. Performing atomic import during enrollment...", request.NodeId);
                var importResult = await ReconcileAndImportUsersAsync(request.NodeId, request.LocalUsersToImport, Context.ConnectionId);
                isIdentityImported = importResult.Success;
            }

            var resolvedClient = !string.IsNullOrWhiteSpace(existingNode?.Client) ? existingNode.Client : request.Client;
            var resolvedBuilding = !string.IsNullOrWhiteSpace(existingNode?.Building) ? existingNode.Building : request.Building;
            var resolvedRoom = !string.IsNullOrWhiteSpace(existingNode?.Room) ? existingNode.Room : request.Room;

            var resolvedNodeName = existingNode?.Name ?? request.NodeId;
            var resolvedStatus = FederatedNodeStatus.ResolveFromRegistration(request.RequiresOperatorSetup);
            var debugLogging = existingNode?.IsDebugLoggingEnabled ?? false;
            if (ScanSettingsJsonHelper.TryReadDebugLoggingEnabled(request.ScanSettingsJson, out var reportedDebug))
                debugLogging = reportedDebug;

            _nodeRepo.UpsertNode(new FederatedNode
            {
                Id = request.NodeId,
                Name = resolvedNodeName,
                Status = resolvedStatus,
                IPAddress = remoteIp,
                HttpPort = AppliancePortDefaults.ResolveHttpPortForNode(
                    request.HttpPort > 0 ? request.HttpPort : AppliancePortDefaults.StandardHttpPort,
                    request.OS),
                HttpsPort = AppliancePortDefaults.ResolveHttpsPortForNode(request.HttpsPort),
                LastSeenUtc = DateTime.UtcNow,
                ConnectionId = Context.ConnectionId,
                Version = request.Version,
                OS = request.OS,
                LicenseTier = request.LicenseTier,
                IsPortable = request.IsPortable,
                Client = resolvedClient,
                Building = resolvedBuilding,
                Room = resolvedRoom,
                SyncUsers = syncUsers,
                SyncUserRegistry = syncUserRegistry,
                SyncSsoSettings = syncSsoSettings,
                SyncAlertSettings = syncAlertSettings,
                SyncSiemSettings = syncSiemSettings,
                OverrideAlertSettings = request.OverrideAlertSettings,
                OverrideSiemSettings = request.OverrideSiemSettings,
                ScanSettingsJson = reconciledScanSettings,
                IsDebugLoggingEnabled = debugLogging,
                IsIdentityImported = isIdentityImported,
                DatabaseSize = request.DatabaseSize,
                HardwareId = targetHwid,
                DelegateAlertDispatch = existingNode?.DelegateAlertDispatch ?? false,
                CertificateSerialNumber = existingNode?.CertificateSerialNumber,
                IsRevoked = existingNode?.IsRevoked ?? false
            });

            var connectionId = Context.ConnectionId;
            var nodeId = request.NodeId;

            await Groups.AddToGroupAsync(connectionId, $"Node_{nodeId}");
            
            _logger.LogInformation("SignalR: Node {NodeId} successfully registered and added to routing group.", nodeId);

            if (_remoteExecution != null)
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(500);
                    try
                    {
                        await _remoteExecution.FlushPendingGovernanceToNodeAsync(nodeId, connectionId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "SignalR: Failed to flush pending governance to Node {NodeId}", nodeId);
                    }
                });
            }

            // If Hub has different scan settings than what Node registered with, force push settings to Node!
            if (!string.IsNullOrEmpty(reconciledScanSettings) && reconciledScanSettings != request.ScanSettingsJson)
            {
                _logger.LogInformation("SignalR: Hub has stored scan settings mismatch for Node {NodeId}. Push-governing Node config...", nodeId);
                _ = Task.Run(async () =>
                {
                    await Task.Delay(1000); // Allow connection handshake to settle
                    try
                    {
                        var command = new FederationCommand
                        {
                            CommandType = "ConfigureScanning",
                            Parameters = new Dictionary<string, string> { { "ScanSettingsJson", reconciledScanSettings } }
                        };
                        await _hubContext.Clients.Client(connectionId).SendAsync("ExecuteCommand", command);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "SignalR: Failed to auto-govern scan settings for Node {NodeId}", nodeId);
                    }
                });
            }

            // Sync initial IAM/SSO state immediately to this Node
            _ = Task.Run(async () => await _identitySync.PushIdentityStateToNodeAsync(connectionId));

            // Sync initial Alerts/SIEM settings to this Node
            _ = Task.Run(async () => await _settingsSync.PushSettingsStateToNodeAsync(connectionId));

            // Build the InheritedLicensePayload
            var payload = new StacksAtlas.Core.Services.Licensing.InheritedLicensePayload(
                request.NodeId,
                licenseStatus.Tier.ToString(),
                DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            );
            
            var payloadJson = System.Text.Json.JsonSerializer.Serialize(payload);
            var payloadBytes = System.Text.Encoding.UTF8.GetBytes(payloadJson);
            var signature = _hubCa.SignData(payloadBytes);

            return new FederationRegistrationResponse
            {
                Success = true,
                LastKnownSyncUtc = lastSync ?? DateTime.MinValue,
                SyncUsers = syncUsers,
                SyncUserRegistry = syncUserRegistry,
                SyncSsoSettings = syncSsoSettings,
                SyncAlertSettings = syncAlertSettings,
                SyncSiemSettings = syncSiemSettings,
                DelegateAlertDispatch = existingNode?.DelegateAlertDispatch ?? false,
                InheritedLicensePayloadJson = payloadJson,
                InheritedLicenseSignature = signature,
                Client = resolvedClient,
                Building = resolvedBuilding,
                Room = resolvedRoom,
                NodeDisplayName = resolvedNodeName
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SignalR: Error during AuthenticateNode for {NodeId}", request.NodeId);
            return new FederationRegistrationResponse { Success = false, Message = ex.Message };
        }
    }

    /// <summary>
    /// Lightweight Node check-in for scan settings + adapter telemetry without a full re-registration.
    /// </summary>
    public Task<bool> ReportScanSettings(string scanSettingsJson)
    {
        if (!FederationSignalRRegistry.TryGetNodeId(Context.ConnectionId, out var nodeId))
        {
            _logger.LogWarning("SignalR: Unregistered node attempted to report scan settings. ConnectionId: {ConnectionId}", Context.ConnectionId);
            return Task.FromResult(false);
        }

        try
        {
            var existing = _nodeRepo.GetById(nodeId);
            if (existing == null)
                return Task.FromResult(false);

            var reconciled = ScanSettingsJsonHelper.ReconcileScanSettings(existing.ScanSettingsJson, scanSettingsJson);
            existing.ScanSettingsJson = reconciled;
            if (ScanSettingsJsonHelper.TryReadDebugLoggingEnabled(scanSettingsJson, out var debugEnabled))
                existing.IsDebugLoggingEnabled = debugEnabled;
            existing.LastSeenUtc = DateTime.UtcNow;
            _nodeRepo.UpsertNode(existing);

            _logger.LogDebug("SignalR: Updated scan settings telemetry for Node {NodeId}", nodeId);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SignalR: Failed to process scan settings report from Node {NodeId}", nodeId);
            return Task.FromResult(false);
        }
    }


    /// <summary>
    /// Called by the Node to push a device discovery or status delta to the Hub.
    /// </summary>
    public async Task PushTelemetry(List<Device> devices)
    {
        if (!FederationSignalRRegistry.TryGetNodeId(Context.ConnectionId, out var nodeId))
        {
            _logger.LogWarning("SignalR: Unregistered node attempted to push data. ConnectionId: {ConnectionId}", Context.ConnectionId);
            throw new HubException("Node must register before pushing telemetry.");
        }

        _logger.LogTrace("SignalR: Received telemetry batch ({Count} devices) from Node {NodeId}", devices.Count, nodeId);

        if (devices.Any(d => d.IsLifecycleGovernancePush))
        {
            _logger.LogInformation(
                "SignalR: Lifecycle governance telemetry ({Count} device(s)) from Node {NodeId}",
                devices.Count(d => d.IsLifecycleGovernancePush),
                nodeId);
        }

        var node = _nodeRepo.GetById(nodeId);
        DateTime maxModified = devices.Count > 0 ? devices.Max(d => d.LastModifiedUtc) : DateTime.MinValue;

        foreach (var device in devices)
        {
            device.NodeId = nodeId;
            // Fleet Hierarchy Tagging
            device.Client ??= node?.Client;
            device.Building ??= node?.Building;
            device.Room ??= node?.Room;
            if (device.LastModifiedUtc > maxModified) maxModified = device.LastModifiedUtc;
        }

        // Queue telemetry in-memory for high-throughput background bulk writing
        _ingestionBuffer.QueueDevices(devices);

        // Heartbeat + sync cursor  -  background only (never block PushTelemetry on SQLite writes)
        if (node != null)
        {
            var lastSync = maxModified > node.LastSyncUtc ? maxModified : node.LastSyncUtc;
            _ingestionBuffer.QueueNodeHeartbeat(nodeId, DateTime.UtcNow, lastSync);
        }
    }

    /// <summary>
    /// Lightweight liveness pulse from a registered Node (no device payload).
    /// Keeps Hub node status online while a full snapshot backlog is draining.
    /// </summary>
    public Task PushNodePulse(bool requiresOperatorSetup = true)
    {
        if (!FederationSignalRRegistry.TryGetNodeId(Context.ConnectionId, out var nodeId))
        {
            _logger.LogWarning("SignalR: Unregistered node attempted to push pulse. ConnectionId: {ConnectionId}", Context.ConnectionId);
            throw new HubException("Node must register before pushing pulse.");
        }

        var node = _nodeRepo.GetById(nodeId);
        if (node != null)
        {
            var statusUpdate = FederatedNodeStatus.ResolveAfterOperationalPulse(node.Status, requiresOperatorSetup);
            if (statusUpdate != null)
            {
                node.Status = statusUpdate;
                _nodeRepo.UpsertNode(node);
                _logger.LogInformation(
                    "SignalR: Node {NodeId} operational pulse updated fleet status to {Status}.",
                    nodeId,
                    statusUpdate);
            }

            _ingestionBuffer.QueueNodeHeartbeat(nodeId, DateTime.UtcNow, node.LastSyncUtc);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Called by the Node to stream diagnostic and warning logs to the Hub.
    /// </summary>
    public async Task PushLogs(List<FederatedLog> logs)
    {
        if (!FederationSignalRRegistry.TryGetNodeId(Context.ConnectionId, out var nodeId))
        {
            _logger.LogWarning("SignalR: Unregistered node attempted to push logs. ConnectionId: {ConnectionId}", Context.ConnectionId);
            throw new HubException("Node must register before pushing logs.");
        }

        if (logs == null || logs.Count == 0) return;

        _logger.LogTrace("SignalR: Received log batch ({Count} logs) from Node {NodeId}", logs.Count, nodeId);

        foreach (var log in logs)
        {
            log.NodeId = nodeId;
            log.Id = 0; // Reset for auto-increment in SQLite
        }

        // Queue diagnostic logs in-memory for high-throughput background bulk writing
        _ingestionBuffer.QueueLogs(logs);
    }

    /// <summary>
    /// Called by the Node to push system events to the Hub.
    /// </summary>
    public async Task PushEvents(List<SystemEvent> events)
    {
        if (!FederationSignalRRegistry.TryGetNodeId(Context.ConnectionId, out var nodeId))
        {
            _logger.LogWarning("SignalR: Unregistered node attempted to push events. ConnectionId: {ConnectionId}", Context.ConnectionId);
            throw new HubException("Node must register before pushing events.");
        }

        if (events == null || events.Count == 0) return;

        _logger.LogTrace("SignalR: Received system event batch ({Count} events) from Node {NodeId}", events.Count, nodeId);

        var node = _nodeRepo.GetById(nodeId);

        foreach (var evt in events)
        {
            if (evt.Id == LiteDB.ObjectId.Empty)
                evt.Id = LiteDB.ObjectId.NewObjectId();

            evt.NodeId = nodeId;
            if (node != null)
            {
                evt.NodeName = node.Name;
                evt.Client = node.Client;
                evt.Building = node.Building;
                evt.Room = node.Room;
            }
        }

        _ingestionBuffer.QueueSystemEvents(events);
    }

    /// <summary>
    /// Called by the Node to push admin audit events to the Hub.
    /// </summary>
    public async Task PushAuditEvents(List<AuditEvent> events)
    {
        if (!FederationSignalRRegistry.TryGetNodeId(Context.ConnectionId, out var nodeId))
        {
            _logger.LogWarning("SignalR: Unregistered node attempted to push audit events. ConnectionId: {ConnectionId}", Context.ConnectionId);
            throw new HubException("Node must register before pushing audit events.");
        }

        if (events == null || events.Count == 0) return;

        _logger.LogTrace("SignalR: Received audit event batch ({Count} events) from Node {NodeId}", events.Count, nodeId);

        var node = _nodeRepo.GetById(nodeId);

        foreach (var evt in events)
        {
            StacksAtlas.Core.Services.Audit.AuditEventNormalizer.Normalize(evt);
            evt.NodeId = nodeId;
            if (node != null)
                evt.NodeName = node.Name;
        }

        _ingestionBuffer.QueueAuditEvents(events);
    }

    /// <summary>
    /// Called by the Node to push/delegate alert notifications to the Hub.
    /// </summary>
    public async Task PushAlerts(List<PendingAlertDto> pendingAlerts)
    {
        if (!FederationSignalRRegistry.TryGetNodeId(Context.ConnectionId, out var nodeId))
        {
            _logger.LogWarning("SignalR: Unregistered node attempted to push alerts. ConnectionId: {ConnectionId}", Context.ConnectionId);
            throw new HubException("Node must register before pushing alerts.");
        }

        if (pendingAlerts == null || pendingAlerts.Count == 0) return;

        _logger.LogTrace("SignalR: Received alert batch ({Count} alerts) from Node {NodeId}", pendingAlerts.Count, nodeId);

        var directAlertEvents = new List<AlertEvent>();

        using var scope = Context.GetHttpContext()?.RequestServices.CreateScope();
        var alertService = scope?.ServiceProvider.GetService<IAlertService>();
        var node = _nodeRepo.GetById(nodeId);

        foreach (var pa in pendingAlerts)
        {
            pa.Alert.NodeId = nodeId;
            if (node != null)
            {
                pa.Alert.NodeName = node.Name;
                pa.Alert.Client = node.Client;
                pa.Alert.Building = node.Building;
                pa.Alert.Room = node.Room;
            }

            if (pa.IsHistoricalReplay)
            {
                pa.Alert.Success = false;
                pa.Alert.ErrorMessage ??= "Hub offline  -  notification not dispatched (historical replay)";
                directAlertEvents.Add(pa.Alert);
                continue;
            }

            if (pa.IsDelegation)
            {
                if (alertService != null && pa.Device != null)
                {
                    _logger.LogInformation("SignalR: Node {NodeId} requested central delegation for alert event type {AlertType}", nodeId, pa.Alert.AlertType);
                    var dev = new Device
                    {
                        Id = pa.Device.Id,
                        IpAddress = pa.Device.IpAddress,
                        MacAddress = pa.Device.MacAddress,
                        Name = pa.Device.Name,
                        ManagedByUserId = pa.Device.ManagedByUserId,
                        AlertsEnabled = pa.Device.AlertsEnabled,
                        NodeId = nodeId,
                        Client = node?.Client,
                        Building = node?.Building,
                        Room = node?.Room,
                        FirstDiscoveredUtc = pa.Device.FirstDiscoveredUtc ?? DateTime.MinValue
                    };
                    await alertService.DelegateAlertAsync(pa.Alert.AlertType, dev, nodeId);
                }
                else
                {
                    _logger.LogWarning("SignalR: Failed to process delegated alert for Node {NodeId} (AlertService or Device details missing)", nodeId);
                }
            }
            else
            {
                directAlertEvents.Add(pa.Alert);
            }
        }

        if (directAlertEvents.Count > 0)
        {
            _ingestionBuffer.QueueAlertEvents(directAlertEvents);
        }
    }

    /// <summary>
    /// Invoked by Nodes to securely push and migrate their local accounts to the Hub's LiteDB master database.
    /// </summary>
    public async Task<UserImportResult> ImportLocalUsers(List<User> users)
    {
        if (!FederationSignalRRegistry.TryGetNodeId(Context.ConnectionId, out var nodeId))
        {
            _logger.LogWarning("SignalR: Unregistered node attempted to import users. ConnectionId: {ConnectionId}", Context.ConnectionId);
            throw new HubException("Node must register before importing users.");
        }

        _logger.LogInformation("SignalR: Received user import request ({Count} users) from Node {NodeId}", users?.Count ?? 0, nodeId);
        var result = await ReconcileAndImportUsersAsync(nodeId, users, Context.ConnectionId);

        // Mark this node as migrated in the database
        var node = _nodeRepo.GetById(nodeId);
        if (node != null)
        {
            node.IsIdentityImported = result.Success;
            node.LastSeenUtc = DateTime.UtcNow;
            _nodeRepo.UpsertNode(node);
        }

        return result;
    }

    private async Task<UserImportResult> ReconcileAndImportUsersAsync(string nodeId, List<User>? localUsers, string connectionId)
    {
        var result = new UserImportResult { Success = true };
        if (localUsers == null || localUsers.Count == 0)
        {
            result.Message = "No users to import.";
            return result;
        }

        try
        {
            var userCol = _db.GetCollection<User>("users");
            int imported = 0;
            int collisions = 0;

            foreach (var u in localUsers)
            {
                // Security Guardrail: Skip standard emergency 'admin'
                if (u.Username.ToLower() == "admin")
                {
                    continue;
                }

                // Check for collision in Hub's database
                var existing = userCol.FindOne(x => x.Username.ToLower() == u.Username.ToLower());
                if (existing != null)
                {
                    // Option A: Collision - append unique NodeId suffix to imported account
                    var newUsername = $"{u.Username}_{nodeId}";
                    
                    // Double check if suffix-appended username is also taken (unlikely, but let's be safe)
                    var doubleCheck = userCol.FindOne(x => x.Username.ToLower() == newUsername.ToLower());
                    if (doubleCheck != null)
                    {
                        collisions++;
                        result.CollidedUsernames.Add(u.Username);
                        _logger.LogWarning("FederationHub: Skipping import for colliding user '{Username}' because suffix '{Suffix}' is also taken.", u.Username, newUsername);
                        continue;
                    }

                    u.Username = newUsername;
                    collisions++;
                    result.CollidedUsernames.Add(u.Username);
                    _logger.LogInformation("FederationHub: Imported colliding user '{Original}' as '{Suffix}' for Node {NodeId}", u.Username, newUsername, nodeId);
                }

                // Webhook Preference Reset: Clear local webhook GUIDs to prevent broken references
                u.PreferredWebhookId = null;
                u.PreferredWebhookIds = new List<Guid>();

                // Reset ID to a fresh Guid to prevent ID clashes in master database
                u.Id = Guid.NewGuid();
                u.OriginNodeId = nodeId;

                // Save to Hub's LiteDB
                userCol.Insert(u);
                imported++;
            }

            result.ImportedCount = imported;
            result.CollisionCount = collisions;
            result.Message = $"Successfully imported {imported} users with {collisions} collisions renamed.";

            // If we successfully imported users, sync identity state down and broadcast update
            _logger.LogInformation("FederationHub: Imported {Count} users from Node {NodeId}. Syncing identity state down to Node...", imported, nodeId);
            _ = Task.Run(async () => 
            {
                await Task.Delay(500); // Allow DB updates to fully commit
                await _identitySync.PushIdentityStateToNodeAsync(connectionId);
                await _identitySync.BroadcastIdentityStateAsync(); // Update all other governed nodes
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FederationHub: Error during user import for Node {NodeId}", nodeId);
            result.Success = false;
            result.Message = $"Import failure: {ex.Message}";
        }

        return result;
    }

    /// <summary>
    /// Invoked by a Node during emergency decoupling to notify the Hub, log an alert, and cleanup database records.
    /// </summary>
    public async Task DecoupleNode()
    {
        if (FederationSignalRRegistry.TryGetNodeId(Context.ConnectionId, out var nodeId))
        {
            var node = _nodeRepo.GetById(nodeId);
            string nodeName = node?.Name ?? nodeId;
            string nodeIp = NetworkHelper.NormalizeIpAddress(node?.IPAddress);

            _logger.LogWarning("SignalR: Node {NodeId} ({NodeName}) initiated emergency local decoupling. Logging alert and pruning records.", nodeId, nodeName);

            // 1. Log a fleet audit event (notification history only  -  not a device-scoped alert dispatch)
            _alertEventRepo.LogAlert(new AlertEvent
            {
                Id = Guid.NewGuid().ToString(),
                TriggeredAt = DateTime.UtcNow,
                DeviceId = nodeId,
                DeviceName = nodeName,
                DeviceIp = nodeIp,
                NodeId = nodeId,
                NodeName = nodeName,
                Client = node?.Client,
                Building = node?.Building,
                Room = node?.Room,
                AlertType = AlertEventType.NodeDecoupled,
                Success = false,
                ErrorMessage = "Fleet audit event  -  notification dispatch not applicable"
            });

            // 2. Remove Node registration from database
            _nodeRepo.Delete(nodeId);

            // 3. Clean up devices and logs associated with the node
            if (_contextFactory != null)
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                var devices = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
                    context.Devices.Where(d => d.NodeId == nodeId)
                );
                if (devices.Count > 0)
                {
                    context.Devices.RemoveRange(devices);
                }

                var logs = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
                    context.FederatedLogs.Where(l => l.NodeId == nodeId)
                );
                if (logs.Count > 0)
                {
                    context.FederatedLogs.RemoveRange(logs);
                }

                await context.SaveChangesAsync();
            }
            else
            {
                var pagedDevices = _deviceRepo.GetAll(true).Where(d => d.NodeId == nodeId).ToList();
                foreach (var d in pagedDevices)
                {
                    _deviceRepo.HardDelete(d.Id);
                }
            }

            // 4. Clean up users imported from this node
            var userCol = _db.GetCollection<User>("users");
            var usersToDelete = userCol.Find(u => u.OriginNodeId == nodeId).ToList();
            foreach (var u in usersToDelete)
            {
                userCol.Delete(u.Id);
            }

            // 5. Broadcast updated identity state to other governed nodes
            await _identitySync.BroadcastIdentityStateAsync();
        }
    }

    private static bool IsPaidFleetNodeTier(string? tier)
    {
        if (string.IsNullOrWhiteSpace(tier))
            return false;
        return tier.Equals("Pro", StringComparison.OrdinalIgnoreCase)
            || tier.Equals("Business", StringComparison.OrdinalIgnoreCase)
            || tier.Equals("Enterprise", StringComparison.OrdinalIgnoreCase);
    }
}
