using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StacksAtlas.API.Extensions;
using StacksAtlas.API.Hubs;
using StacksAtlas.Core.Data;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Services.Scanning;
using StacksAtlas.Core.Services.Devices;
using StacksAtlas.Core.Services.Network;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace StacksAtlas.API.Services;

/// <summary>
/// A service for Hub mode that relays diagnostic commands (Ping, WOL, Deep Scan) 
/// to the appropriate Node via SignalR.
/// </summary>
public class RemoteExecutionService(
    IHubContext<FederationHub> hubContext,
    IDeviceRepository deviceRepo,
    IFederatedNodeRepository nodeRepo,
    ILogger<RemoteExecutionService> logger,
    IServiceProvider serviceProvider) 
    : IHostPinger, IWakeOnLanService, IDeepScanService, ITracerouteService
{
    private readonly IHubContext<FederationHub> _hubContext = hubContext;
    private readonly IDeviceRepository _deviceRepo = deviceRepo;
    private readonly IFederatedNodeRepository _nodeRepo = nodeRepo;
    private readonly ILogger<RemoteExecutionService> _logger = logger;
    private readonly IServiceProvider _serviceProvider = serviceProvider;

    private static readonly JsonSerializerOptions RelayJsonOptions = CreateRelayJsonOptions();
    private static readonly ConcurrentDictionary<string, byte> GovernanceFlushInProgress = new();
    private const int GovernanceFlushDelayMs = 25;

    private static JsonSerializerOptions CreateRelayJsonOptions()
    {
        var options = new JsonSerializerOptions();
        FederationJson.Configure(options);
        return options;
    }

    private static T? DeserializeRelayData<T>(object? data)
    {
        if (data == null) return default;
        var json = JsonSerializer.Serialize(data, RelayJsonOptions);
        return JsonSerializer.Deserialize<T>(json, RelayJsonOptions);
    }

    private static List<T> DeserializeRelayList<T>(object? data)
    {
        return DeserializeRelayData<List<T>>(data) ?? [];
    }

    // --- IHostPinger ---
    public async Task<HostScanResult> PingAsync(string ip, CancellationToken token, bool onDemand = false)
    {
        var device = _deviceRepo.GetByIp(ip);
        if (device == null || string.IsNullOrEmpty(device.NodeId))
        {
            _logger.LogWarning("Remote Ping failed: Device {Ip} not found or is local-only.", ip);
            return HostScanResult.Offline(ip);
        }

        var node = _nodeRepo.GetById(device.NodeId);
        if (node == null || string.IsNullOrEmpty(node.ConnectionId))
        {
            _logger.LogWarning("Remote Ping failed: Node {NodeId} is offline or not registered.", device.NodeId);
            return HostScanResult.Offline(ip);
        }

        try
        {
            var command = new FederationCommand
            {
                CommandType = "Ping",
                TargetId = device.Id.ToString(),
                Parameters = new Dictionary<string, string> { { "Ip", ip } }
            };

            _logger.LogInformation("Relaying Ping command for {Ip} to Node {NodeId} ({ConnectionId})", ip, device.NodeId, node.ConnectionId);

            // Wait for result from Node
            var result = await _hubContext.Clients.Client(node.ConnectionId)
                .InvokeAsync<FederationCommandResult>("ExecuteCommand", command, token);

            if (result.Success && result.Data != null)
            {
                return DeserializeRelayData<HostScanResult>(result.Data) ?? HostScanResult.Offline(ip);
            }

            return HostScanResult.Offline(ip);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Remote Ping failed for {Ip} via Node {NodeId}", ip, device.NodeId);
            return HostScanResult.Offline(ip);
        }
    }

    // --- IWakeOnLanService ---
    public async Task SendMagicPacketAsync(string macAddress)
    {
        var device = _deviceRepo.GetByMac(macAddress);
        if (device == null || string.IsNullOrEmpty(device.NodeId))
        {
            _logger.LogWarning("Remote WOL failed: Device with MAC {Mac} not found.", macAddress);
            return;
        }

        var node = _nodeRepo.GetById(device.NodeId);
        if (node == null || string.IsNullOrEmpty(node.ConnectionId)) return;

        try
        {
            var command = new FederationCommand
            {
                CommandType = "WakeOnLan",
                TargetId = device.Id.ToString(),
                Parameters = new Dictionary<string, string> { { "Mac", macAddress } }
            };

            await _hubContext.Clients.Client(node.ConnectionId)
                .InvokeAsync<FederationCommandResult>("ExecuteCommand", command, default);
            
            _logger.LogInformation("Relayed WOL command for {Mac} to Node {NodeId}", macAddress, device.NodeId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Remote WOL failed for {Mac} via Node {NodeId}", macAddress, device.NodeId);
        }
    }

    // --- IDeepScanService ---
    public void QueueScan(Guid deviceId)
    {
        var device = _deviceRepo.GetById(deviceId);
        if (device == null || string.IsNullOrEmpty(device.NodeId)) return;

        var node = _nodeRepo.GetById(device.NodeId);
        if (node == null || string.IsNullOrEmpty(node.ConnectionId)) return;

        var command = new FederationCommand
        {
            CommandType = "DeepScan",
            TargetId = deviceId.ToString()
        };

        // Fire and forget (relay)
        _ = _hubContext.Clients.Client(node.ConnectionId).InvokeAsync<FederationCommandResult>("ExecuteCommand", command, default);
        _logger.LogInformation("Relayed Deep Scan request for {DeviceId} to Node {NodeId}", deviceId, device.NodeId);
    }

    public (string Status, int Progress) GetScanStatus(Guid deviceId)
    {
        var device = _deviceRepo.GetById(deviceId);
        if (device == null || string.IsNullOrEmpty(device.NodeId))
            return ("idle", 0);

        var node = _nodeRepo.GetById(device.NodeId);
        if (node == null || string.IsNullOrEmpty(node.ConnectionId))
            return ("idle", 0);

        try
        {
            var command = new FederationCommand
            {
                CommandType = "GetDeepScanStatus",
                TargetId = deviceId.ToString()
            };

            var result = _hubContext.Clients.Client(node.ConnectionId)
                .InvokeAsync<FederationCommandResult>("ExecuteCommand", command, CancellationToken.None)
                .GetAwaiter().GetResult();

            if (result.Success && result.Data != null)
            {
                var json = System.Text.Json.JsonSerializer.Serialize(result.Data);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var status = doc.RootElement.TryGetProperty("status", out var s) ? s.GetString() ?? "idle" : "idle";
                var progress = doc.RootElement.TryGetProperty("progress", out var p) ? p.GetInt32() : 0;
                return (status, progress);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch remote deep scan status for {DeviceId}", deviceId);
        }

        return ("idle", 0);
    }

    public IReadOnlyList<ServiceDetail> GetScanResults(Guid deviceId)
    {
        var device = _deviceRepo.GetById(deviceId);
        if (device == null || string.IsNullOrEmpty(device.NodeId))
            return Array.Empty<ServiceDetail>();

        var node = _nodeRepo.GetById(device.NodeId);
        if (node == null || string.IsNullOrEmpty(node.ConnectionId))
            return Array.Empty<ServiceDetail>();

        try
        {
            var command = new FederationCommand
            {
                CommandType = "GetDeepScanResults",
                TargetId = deviceId.ToString(),
            };

            var result = _hubContext.Clients.Client(node.ConnectionId)
                .InvokeAsync<FederationCommandResult>("ExecuteCommand", command, CancellationToken.None)
                .GetAwaiter().GetResult();

            if (result.Success && result.Data != null)
            {
                return DeserializeRelayList<ServiceDetail>(result.Data);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch remote deep scan results for {DeviceId}", deviceId);
        }

        return Array.Empty<ServiceDetail>();
    }

    public IReadOnlyList<LeaseHistory> GetLeaseHistoryByMac(string cleanMac)
    {
        var device = _deviceRepo.GetByMac(cleanMac);
        return RelayLeaseHistory(device, "GetLeaseHistoryByMac", new Dictionary<string, string> { { "Mac", cleanMac } });
    }

    public IReadOnlyList<LeaseHistory> GetLeaseHistoryByIp(string ip)
    {
        var device = _deviceRepo.GetByIp(ip);
        return RelayLeaseHistory(device, "GetLeaseHistoryByIp", new Dictionary<string, string> { { "Ip", ip } });
    }

    public async Task<FederationCommandResult?> RelayAcknowledgeSecurityRiskAsync(
        Device device,
        string issue,
        CancellationToken token = default)
    {
        if (string.IsNullOrEmpty(device.NodeId))
            return null;

        var node = _nodeRepo.GetById(device.NodeId);
        if (node == null || string.IsNullOrEmpty(node.ConnectionId))
        {
            return new FederationCommandResult { Success = false, Message = "Node offline" };
        }

        var command = new FederationCommand
        {
            CommandType = "AcknowledgeSecurityRisk",
            TargetId = device.Id.ToString(),
            Parameters = new Dictionary<string, string> { { "Issue", issue } },
        };

        try
        {
            var result = await _hubContext.Clients.Client(node.ConnectionId)
                .InvokeAsync<FederationCommandResult>("ExecuteCommand", command, token);

            _logger.LogInformation(
                "Relayed AcknowledgeSecurityRisk for device {DeviceId} to Node {NodeId}  -  success={Success}",
                device.Id,
                device.NodeId,
                result.Success);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "AcknowledgeSecurityRisk relay failed for device {DeviceId} via Node {NodeId}",
                device.Id,
                device.NodeId);
            return new FederationCommandResult { Success = false, Message = ex.Message };
        }
    }

    public async Task<FederationCommandResult?> RelayResetMetricsAsync(
        Device device,
        CancellationToken token = default)
    {
        if (string.IsNullOrEmpty(device.NodeId))
            return null;

        var node = _nodeRepo.GetById(device.NodeId);
        if (node == null || string.IsNullOrEmpty(node.ConnectionId))
        {
            return new FederationCommandResult { Success = false, Message = "Node offline" };
        }

        var command = new FederationCommand
        {
            CommandType = "ResetMetrics",
            TargetId = device.Id.ToString(),
        };

        try
        {
            var result = await _hubContext.Clients.Client(node.ConnectionId)
                .InvokeAsync<FederationCommandResult>("ExecuteCommand", command, token);

            _logger.LogInformation(
                "Relayed ResetMetrics for device {DeviceId} to Node {NodeId}  -  success={Success}",
                device.Id,
                device.NodeId,
                result.Success);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "ResetMetrics relay failed for device {DeviceId} via Node {NodeId}",
                device.Id,
                device.NodeId);
            return new FederationCommandResult { Success = false, Message = ex.Message };
        }
    }

    private async Task<(List<TracerouteHop> Hops, string? Error)> RelayTracerouteAsync(
        Device device,
        string targetIp,
        int maxHops,
        int timeout,
        CancellationToken cancellationToken)
    {
        var node = _nodeRepo.GetById(device.NodeId!);
        if (node == null || string.IsNullOrEmpty(node.ConnectionId))
        {
            _logger.LogWarning("Traceroute relay failed: Node {NodeId} offline.", device.NodeId);
            return ([], "The enrolled node is offline. Traceroute must run from that node's network.");
        }

        try
        {
            var command = new FederationCommand
            {
                CommandType = "RunTraceroute",
                TargetId = device.Id.ToString(),
                Parameters = new Dictionary<string, string>
                {
                    { "Ip", targetIp },
                    { "MaxHops", maxHops.ToString() },
                    { "Timeout", timeout.ToString() },
                },
            };

            _logger.LogInformation(
                "Relaying RunTraceroute for {Ip} (device {DeviceId}) to Node {NodeId}",
                targetIp,
                device.Id,
                device.NodeId);

            using var invokeTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            invokeTimeout.CancelAfter(TimeSpan.FromSeconds(90));

            var result = await _hubContext.Clients.Client(node.ConnectionId)
                .InvokeAsync<FederationCommandResult>("ExecuteCommand", command, invokeTimeout.Token);

            if (!result.Success)
            {
                var msg = result.Message ?? "Node rejected traceroute command";
                _logger.LogWarning(
                    "Traceroute relay failed for {Ip} via Node {NodeId}: {Message}",
                    targetIp,
                    device.NodeId,
                    msg);
                if (msg.Contains("Unknown command", StringComparison.OrdinalIgnoreCase))
                {
                    msg = "Node does not support hub traceroute relay yet. Update the enrolled node to the same version as this hub.";
                }
                return ([], msg);
            }

            if (result.Data == null)
                return ([], "Node returned an empty traceroute response.");

            var hops = DeserializeRelayList<TracerouteHop>(result.Data);
            if (hops.Count == 0)
            {
                _logger.LogWarning(
                    "Traceroute relay deserialized to zero hops for {Ip}. Raw payload type: {Type}",
                    targetIp,
                    result.Data.GetType().Name);
                return ([], "Node returned no hops (target unreachable or ICMP blocked on the node's network).");
            }

            return (hops, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return ([], "Traceroute relay was cancelled.");
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "Traceroute relay timed out for {Ip} via Node {NodeId}",
                targetIp,
                device.NodeId);
            return ([], "Traceroute relay timed out waiting for the enrolled node. The node may be busy or reconnecting.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Traceroute relay failed for {Ip} via Node {NodeId}", targetIp, device.NodeId);
            var message = ex.Message;
            if (message.Contains("disconnected", StringComparison.OrdinalIgnoreCase))
            {
                message =
                    "Node disconnected during traceroute (federation link dropped). Retry after the node reconnects, or run trace from the node UI.";
            }
            return ([], $"Traceroute relay failed: {message}");
        }
    }

    /// <summary>Hub API: relay traceroute to the device owning node.</summary>
    public Task<(List<TracerouteHop> Hops, string? Error)> RelayTracerouteForDeviceAsync(
        Device device,
        string targetIp,
        CancellationToken cancellationToken = default,
        int maxHops = 15,
        int timeout = 800) =>
        RelayTracerouteAsync(device, targetIp, maxHops, timeout, cancellationToken);

    private IReadOnlyList<LeaseHistory> RelayLeaseHistory(
        Device? device,
        string commandType,
        Dictionary<string, string> parameters)
    {
        if (device == null || string.IsNullOrEmpty(device.NodeId))
            return Array.Empty<LeaseHistory>();

        var node = _nodeRepo.GetById(device.NodeId);
        if (node == null || string.IsNullOrEmpty(node.ConnectionId))
            return Array.Empty<LeaseHistory>();

        try
        {
            var command = new FederationCommand
            {
                CommandType = commandType,
                TargetId = device.Id.ToString(),
                Parameters = parameters,
            };

            var result = _hubContext.Clients.Client(node.ConnectionId)
                .InvokeAsync<FederationCommandResult>("ExecuteCommand", command, CancellationToken.None)
                .GetAwaiter().GetResult();

            if (result.Success && result.Data != null)
            {
                return DeserializeRelayList<LeaseHistory>(result.Data);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to relay {CommandType} for device {DeviceId}", commandType, device.Id);
        }

        return Array.Empty<LeaseHistory>();
    }

    public bool IsNmapAvailable() => true; // Hub assumes nodes have it if they are nodes.
    
    // --- ITracerouteService ---
    public async IAsyncEnumerable<TracerouteHop> RunTracerouteAsync(string targetIp, int maxHops = 30, int timeout = 1000)
    {
        List<TracerouteHop> hops;
        var device = _deviceRepo.GetByIp(targetIp);

        if (device != null && !string.IsNullOrEmpty(device.NodeId))
        {
            var (relayHops, _) = await RelayTracerouteAsync(device, targetIp, maxHops, timeout, CancellationToken.None);
            hops = relayHops;
        }
        else
        {
            hops = [];
            var localTraceroute = _serviceProvider.GetService<TracerouteService>();
            if (localTraceroute != null)
            {
                await foreach (var hop in localTraceroute.RunTracerouteAsync(targetIp, maxHops, timeout))
                    hops.Add(hop);
            }
            else
            {
                _logger.LogWarning(
                    "Traceroute for {TargetIp}: no owning node and no local traceroute service.",
                    targetIp);
            }
        }

        foreach (var hop in hops)
            yield return hop;
    }

    // --- 2-Way Sync Support ---
    public async Task PushUpdateAsync(Device device)
    {
        if (string.IsNullOrEmpty(device.NodeId)) return;
        var node = _nodeRepo.GetById(device.NodeId);
        if (node == null || string.IsNullOrEmpty(node.ConnectionId))
        {
            _logger.LogWarning(
                "UpdateDevice relay skipped: Node {NodeId} offline for device {DeviceId}",
                device.NodeId,
                device.Id);
            return;
        }

        // Include MAC/IP so the Node can resolve the local row when Hub and Node GUIDs diverge.
        var command = new FederationCommand
        {
            CommandType = "UpdateDevice",
            TargetId = device.Id.ToString(),
            Parameters = new Dictionary<string, string>
            {
                { "MacAddress", DeviceMacNormalizer.Normalize(device.MacAddress) ?? string.Empty },
                { "IpAddress", device.IpAddress ?? string.Empty },
                { "Name", device.Name ?? string.Empty },
                { "Location", device.Location ?? "" },
                { "Vendor", device.Vendor ?? "" },
                { "Model", device.Model ?? "" },
                { "Type", device.Type ?? "" },
                { "IsModelManuallySet", device.IsModelManuallySet.ToString() },
                { "IsVendorManuallySet", device.IsVendorManuallySet.ToString() },
                { "IsTypeManuallySet", device.IsTypeManuallySet.ToString() },
                { "Hostname", device.Hostname ?? "" },
                { "IsHostnameManuallySet", device.IsHostnameManuallySet.ToString() },
                { "SerialNumber", device.SerialNumber ?? "" },
                { "AssetTag", device.AssetTag ?? "" },
                { "FirmwareVersion", device.FirmwareVersion ?? "" },
                { "WarrantyExpiresUtc", device.WarrantyExpiresUtc?.ToString("o") ?? "" },
                { "IsSerialNumberManuallySet", device.IsSerialNumberManuallySet.ToString() },
                { "IsFirmwareVersionManuallySet", device.IsFirmwareVersionManuallySet.ToString() },
                { "IsAssetTagManuallySet", device.IsAssetTagManuallySet.ToString() },
                { "IsWarrantyExpiresManuallySet", device.IsWarrantyExpiresManuallySet.ToString() },
                { "AssetMetadataSource", ((int)device.AssetMetadataSource).ToString() },
                { "ManagedByUserId", device.ManagedByUserId?.ToString() ?? "" },
                { "ManagedByUsername", device.ManagedByUsername ?? "" },
                { "AlertsEnabled", device.AlertsEnabled.ToString() },
                { "AttachmentKind", device.AttachmentKind ?? "Unknown" },
                { "AttachmentPort", device.AttachmentPort ?? "" },
                { "AttachmentParentDeviceId", device.AttachmentParentDeviceId?.ToString() ?? "" },
                { "AttachmentParentMac", device.AttachmentParentMac
                    ?? (device.AttachmentParentDeviceId is Guid pid
                        ? DeviceMacNormalizer.Normalize(_deviceRepo.GetById(pid)?.MacAddress) ?? ""
                        : "") },
                { "AttachmentParentIp", device.AttachmentParentIp
                    ?? (device.AttachmentParentDeviceId is Guid parentId
                        ? _deviceRepo.GetById(parentId)?.IpAddress ?? ""
                        : "") },
                { "IsAttachmentManuallySet", device.IsAttachmentManuallySet.ToString() }
            }
        };

        try
        {
            var result = await _hubContext.Clients.Client(node.ConnectionId)
                .InvokeAsync<FederationCommandResult>("ExecuteCommand", command, default);
            if (result is { Success: false })
            {
                _logger.LogWarning(
                    "UpdateDevice relay rejected for device {DeviceId} on Node {NodeId}: {Message}",
                    device.Id,
                    device.NodeId,
                    result.Message);
                return;
            }

            _logger.LogInformation(
                "Relayed UpdateDevice command for {DeviceId} to Node {NodeId}",
                device.Id,
                device.NodeId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "UpdateDevice relay failed for device {DeviceId} via Node {NodeId}",
                device.Id,
                device.NodeId);
        }
    }

    /// <summary>
    /// Soft-archive a federated device on the owning Node (§7.9 Phase A).
    /// </summary>
    public Task<FederationCommandResult?> RelayArchiveDeviceAsync(Device device, CancellationToken token = default) =>
        RelayDeviceLifecycleAsync(device, "ArchiveDevice", token);

    /// <summary>
    /// Restore an archived federated device on the owning Node (§7.9 Phase A).
    /// </summary>
    public Task<FederationCommandResult?> RelayRestoreDeviceAsync(Device device, CancellationToken token = default) =>
        RelayDeviceLifecycleAsync(device, "RestoreDevice", token);

    /// <summary>
    /// Permanently remove (tombstone) a federated device on the owning Node (§7.9 Phase B).
    /// </summary>
    public Task<FederationCommandResult?> RelayRemoveFromFleetAsync(
        Device device,
        string? removedBy = null,
        CancellationToken token = default) =>
        RelayDeviceLifecycleAsync(device, "RemoveFromFleet", token, removedBy);

    /// <summary>
    /// Push Hub governance state (archive / tombstone) to a Node after reconnect (§7.9 Phase D).
    /// </summary>
    public async Task FlushPendingGovernanceToNodeAsync(string nodeId, string connectionId, CancellationToken token = default)
    {
        if (!GovernanceFlushInProgress.TryAdd(nodeId, 0))
        {
            _logger.LogDebug("Governance flush already running for Node {NodeId}; skipping duplicate.", nodeId);
            return;
        }

        try
        {
            var pending = _deviceRepo.GetAll(includeDeleted: true, includePermanentlyRemoved: true)
                .Where(d => string.Equals(d.NodeId, nodeId, StringComparison.OrdinalIgnoreCase)
                            && (d.IsDeleted || d.IsPermanentlyRemoved))
                .ToList();

            if (pending.Count == 0)
                return;

            _logger.LogInformation(
                "Flushing {Count} pending governance device(s) to Node {NodeId} after reconnect.",
                pending.Count,
                nodeId);

            foreach (var device in pending)
            {
                if (token.IsCancellationRequested)
                    break;

                var node = _nodeRepo.GetById(nodeId);
                if (node == null || !string.Equals(node.ConnectionId, connectionId, StringComparison.Ordinal))
                {
                    _logger.LogWarning(
                        "Governance flush stopped for Node {NodeId}: connection changed or node offline.",
                        nodeId);
                    break;
                }

                var commandType = device.IsPermanentlyRemoved ? "RemoveFromFleet" : "ArchiveDevice";
                var command = new FederationCommand
                {
                    CommandType = commandType,
                    TargetId = device.Id.ToString(),
                    Parameters = BuildLifecycleParameters(device, device.RemovedBy)
                };

                try
                {
                    await _hubContext.Clients.Client(connectionId)
                        .SendAsync("ExecuteCommand", command, token);
                }
                catch (IOException ex)
                {
                    _logger.LogWarning(ex,
                        "Governance flush aborted for Node {NodeId} after disconnect on {CommandType} for device {DeviceId}",
                        nodeId,
                        commandType,
                        device.Id);
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to flush {CommandType} for device {DeviceId} to Node {NodeId}",
                        commandType,
                        device.Id,
                        nodeId);
                }

                if (GovernanceFlushDelayMs > 0)
                    await Task.Delay(GovernanceFlushDelayMs, token);
            }
        }
        finally
        {
            GovernanceFlushInProgress.TryRemove(nodeId, out _);
        }
    }

    /// <summary>
    /// Re-admit a tombstoned federated device on the owning Node (§7.9 Phase B).
    /// </summary>
    public Task<FederationCommandResult?> RelayRestoreFromFleetAsync(Device device, CancellationToken token = default) =>
        RelayDeviceLifecycleAsync(device, "RestoreFromFleet", token);

    private async Task<FederationCommandResult?> RelayDeviceLifecycleAsync(
        Device device,
        string commandType,
        CancellationToken token,
        string? removedBy = null)
    {
        if (string.IsNullOrEmpty(device.NodeId))
            return null;

        var node = _nodeRepo.GetById(device.NodeId);
        if (node == null || string.IsNullOrEmpty(node.ConnectionId))
        {
            _logger.LogWarning(
                "Lifecycle relay skipped: Node {NodeId} offline for device {DeviceId} ({CommandType})",
                device.NodeId, device.Id, commandType);
            return new FederationCommandResult { Success = false, Message = "Node offline" };
        }

        var command = new FederationCommand
        {
            CommandType = commandType,
            TargetId = device.Id.ToString(),
            Parameters = BuildLifecycleParameters(device, removedBy)
        };

        try
        {
            var result = await _hubContext.Clients.Client(node.ConnectionId)
                .InvokeAsync<FederationCommandResult>("ExecuteCommand", command, token);

            _logger.LogInformation(
                "Relayed {CommandType} for device {DeviceId} to Node {NodeId}  -  success={Success}",
                commandType, device.Id, device.NodeId, result.Success);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Lifecycle relay {CommandType} failed for device {DeviceId} via Node {NodeId}",
                commandType, device.Id, device.NodeId);
            return new FederationCommandResult { Success = false, Message = ex.Message };
        }
    }

    private static Dictionary<string, string> BuildLifecycleParameters(Device device, string? removedBy = null)
    {
        var parameters = new Dictionary<string, string>
        {
            { "MacAddress", DeviceMacNormalizer.Normalize(device.MacAddress) ?? string.Empty },
            { "IpAddress", device.IpAddress ?? string.Empty }
        };
        if (!string.IsNullOrWhiteSpace(removedBy))
            parameters["RemovedBy"] = removedBy;
        return parameters;
    }
}
