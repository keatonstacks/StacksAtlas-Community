using Microsoft.AspNetCore.SignalR;
using StacksAtlas.API.Extensions;
using StacksAtlas.API.Hubs;
using StacksAtlas.Core.Data;
using StacksAtlas.Core.Models;
using System.Text.Json;

namespace StacksAtlas.API.Services;

/// <summary>
/// Hub-mode relay for OpenAVC operations  -  credentials and links stay on the owning Node.
/// </summary>
public class OpenAvcRelayService(
    IHubContext<FederationHub> hubContext,
    IDeviceRepository deviceRepo,
    IFederatedNodeRepository nodeRepo,
    ILogger<OpenAvcRelayService> logger)
{
    private static readonly JsonSerializerOptions RelayJsonOptions = CreateRelayJsonOptions();

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

    public async Task<(OpenAvcDrawerContext? Context, string? Error)> GetDrawerContextAsync(
        Guid deviceId,
        string? targetIp,
        string? hostname = null,
        string? macAddress = null,
        CancellationToken cancellationToken = default)
    {
        var device = deviceRepo.GetById(deviceId);
        if (device == null)
            return (null, "Device not found.");

        if (string.IsNullOrEmpty(device.NodeId))
        {
            return (new OpenAvcDrawerContext { HubRelay = false }, null);
        }

        var command = new FederationCommand
        {
            CommandType = "OpenAvcGetDrawerContext",
            TargetId = deviceId.ToString(),
            Parameters = new Dictionary<string, string>
            {
                { "Ip", targetIp ?? device.IpAddress ?? "" },
                { "Hostname", hostname ?? device.Hostname ?? "" },
                { "Mac", macAddress ?? device.MacAddress ?? "" },
            },
        };

        var result = await InvokeOnNodeAsync(device, command, TimeSpan.FromSeconds(30), cancellationToken);
        if (!result.Success)
            return (null, result.Message ?? "OpenAVC relay failed.");

        var context = DeserializeRelayData<OpenAvcDrawerContext>(result.Data);
        if (context != null)
            context.HubRelay = true;
        return (context, null);
    }

    public async Task<(bool Success, string? Message, object? Result)> SendDeviceCommandAsync(
        Guid deviceId,
        string command,
        Dictionary<string, object>? parameters,
        CancellationToken cancellationToken = default)
    {
        var device = deviceRepo.GetById(deviceId);
        if (device == null)
            return (false, "Device not found.", null);
        if (string.IsNullOrEmpty(device.NodeId))
            return (false, "Device has no owning node.", null);

        var commandParams = new Dictionary<string, string> { { "Command", command } };
        if (parameters != null && parameters.Count > 0)
            commandParams["ParamsJson"] = JsonSerializer.Serialize(parameters);

        var fedCommand = new FederationCommand
        {
            CommandType = "OpenAvcDeviceCommand",
            TargetId = deviceId.ToString(),
            Parameters = commandParams,
        };

        var result = await InvokeOnNodeAsync(device, fedCommand, TimeSpan.FromSeconds(60), cancellationToken);
        return (result.Success, result.Message ?? (result.Success ? "Command completed." : "OpenAVC command failed."), result.Data);
    }

    public async Task<(bool Success, string? Message, object? Result)> ExecuteMacroAsync(
        Guid deviceId,
        string macroId,
        CancellationToken cancellationToken = default)
    {
        var device = deviceRepo.GetById(deviceId);
        if (device == null)
            return (false, "Device not found.", null);
        if (string.IsNullOrEmpty(device.NodeId))
            return (false, "Device has no owning node.", null);

        var fedCommand = new FederationCommand
        {
            CommandType = "OpenAvcExecuteMacro",
            TargetId = deviceId.ToString(),
            Parameters = new Dictionary<string, string> { { "MacroId", macroId } },
        };

        var result = await InvokeOnNodeAsync(device, fedCommand, TimeSpan.FromMinutes(2).Add(TimeSpan.FromSeconds(15)), cancellationToken);
        return (result.Success, result.Message ?? (result.Success ? "Macro sent." : "OpenAVC macro failed."), result.Data);
    }

    public async Task<(bool Success, string? Message)> SaveMacroPinsAsync(
        Guid deviceId,
        IReadOnlyList<string> macroIds,
        CancellationToken cancellationToken = default)
    {
        var device = deviceRepo.GetById(deviceId);
        if (device == null)
            return (false, "Device not found.");
        if (string.IsNullOrEmpty(device.NodeId))
            return (false, "Device has no owning node.");

        var fedCommand = new FederationCommand
        {
            CommandType = "OpenAvcSaveMacroPins",
            TargetId = deviceId.ToString(),
            Parameters = new Dictionary<string, string>
            {
                { "MacroIdsJson", JsonSerializer.Serialize(macroIds) },
                { "Mac", device.MacAddress ?? "" },
                { "Ip", device.IpAddress ?? "" },
            },
        };

        var result = await InvokeOnNodeAsync(device, fedCommand, TimeSpan.FromSeconds(30), cancellationToken);
        return (result.Success, result.Message ?? (result.Success ? "Macro pins saved." : "Failed to save macro pins."));
    }

    public async Task<(OpenAvcDeviceLink? Link, string? Error)> SaveLinkAsync(
        Guid deviceId,
        string openAvcDeviceId,
        string? linkedBy,
        CancellationToken cancellationToken = default)
    {
        var device = deviceRepo.GetById(deviceId);
        if (device == null)
            return (null, "Device not found.");
        if (string.IsNullOrEmpty(device.NodeId))
            return (null, "Device has no owning node.");

        var fedCommand = new FederationCommand
        {
            CommandType = "OpenAvcSaveLink",
            TargetId = deviceId.ToString(),
            Parameters = new Dictionary<string, string>
            {
                { "OpenAvcDeviceId", openAvcDeviceId },
                { "LinkedBy", linkedBy ?? "" },
                { "Mac", device.MacAddress ?? "" },
                { "Ip", device.IpAddress ?? "" },
            },
        };

        var result = await InvokeOnNodeAsync(device, fedCommand, TimeSpan.FromSeconds(30), cancellationToken);
        if (!result.Success)
            return (null, result.Message ?? "Failed to save link on node.");

        return (DeserializeRelayData<OpenAvcDeviceLink>(result.Data), null);
    }

    public async Task<(bool Success, string? Message)> DeleteLinkAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        var device = deviceRepo.GetById(deviceId);
        if (device == null)
            return (false, "Device not found.");
        if (string.IsNullOrEmpty(device.NodeId))
            return (false, "Device has no owning node.");

        var fedCommand = new FederationCommand
        {
            CommandType = "OpenAvcDeleteLink",
            TargetId = deviceId.ToString(),
        };

        var result = await InvokeOnNodeAsync(device, fedCommand, TimeSpan.FromSeconds(15), cancellationToken);
        return (result.Success, result.Message);
    }

    public async Task<List<OpenAvcLinkSummary>> GetAllLinkSummariesAsync(CancellationToken cancellationToken = default)
    {
        var summaries = new List<OpenAvcLinkSummary>();
        var nodes = nodeRepo.GetAll()
            .Where(n => n.Status == "online" && !string.IsNullOrEmpty(n.ConnectionId))
            .ToList();

        foreach (var node in nodes)
        {
            try
            {
                var command = new FederationCommand { CommandType = "OpenAvcGetAllLinks" };
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));

                var result = await hubContext.Clients.Client(node.ConnectionId!)
                    .InvokeAsync<FederationCommandResult>("ExecuteCommand", command, timeout.Token);

                if (!result.Success || result.Data == null)
                    continue;

                var nodeLinks = DeserializeRelayData<List<OpenAvcLinkSummary>>(result.Data);
                if (nodeLinks != null)
                    summaries.AddRange(nodeLinks);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "OpenAVC link summary relay failed for Node {NodeId}", node.Id);
            }
        }

        return summaries;
    }

    private async Task<FederationCommandResult> InvokeOnNodeAsync(
        Device device,
        FederationCommand command,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var node = nodeRepo.GetById(device.NodeId!);
        if (node == null || string.IsNullOrEmpty(node.ConnectionId))
        {
            logger.LogWarning("OpenAVC relay failed: Node {NodeId} offline for device {DeviceId}", device.NodeId, device.Id);
            return new FederationCommandResult
            {
                Success = false,
                Message = "The enrolled node is offline. OpenAVC control requires the site node to be connected.",
            };
        }

        try
        {
            using var invokeTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            invokeTimeout.CancelAfter(timeout);

            logger.LogInformation(
                "Relaying OpenAVC {CommandType} for device {DeviceId} to Node {NodeId}",
                command.CommandType,
                device.Id,
                device.NodeId);

            var result = await hubContext.Clients.Client(node.ConnectionId)
                .InvokeAsync<FederationCommandResult>("ExecuteCommand", command, invokeTimeout.Token);

            if (!result.Success && result.Message?.Contains("Unknown command", StringComparison.OrdinalIgnoreCase) == true)
            {
                result.Message = "Node does not support OpenAVC relay yet. Update the enrolled node to v1.8.5+.";
            }

            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new FederationCommandResult { Success = false, Message = "OpenAVC relay timed out." };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "OpenAVC relay failed for device {DeviceId} via Node {NodeId}", device.Id, device.NodeId);
            return new FederationCommandResult { Success = false, Message = ex.Message };
        }
    }
}
