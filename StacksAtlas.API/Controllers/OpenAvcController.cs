using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StacksAtlas.API.Services;
using StacksAtlas.Core.Data;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Services.Integrations;
using StacksAtlas.Core.State;
using StacksAtlas.Core.Services.Audit;

namespace StacksAtlas.API.Controllers;

[Authorize]
[ApiController]
[Route("api/integrations/openavc")]
public class OpenAvcController(
    OpenAvcService openAvcService,
    IServiceProvider serviceProvider,
    IDeviceRepository deviceRepo,
    IAuditService audit,
    ILogger<OpenAvcController> logger) : ControllerBase
{
    private readonly IAuditService _audit = audit;
    private OpenAvcRelayService? Relay => serviceProvider.GetService<OpenAvcRelayService>();

    private IActionResult? RejectIfPortable()
    {
        if (!ExecutionState.IsPortable)
            return null;

        return StatusCode(403, new
        {
            message = "OpenAVC control requires a permanent Business install. Portable mode is discovery-only.",
        });
    }

    [HttpGet("candidates")]
    public async Task<IActionResult> GetNetworkCandidates(CancellationToken cancellationToken)
    {
        if (RejectIfPortable() is { } portable)
            return portable;

        if (IsHubBrain())
            return Ok(Array.Empty<OpenAvcNetworkCandidate>());

        var candidates = new List<OpenAvcNetworkCandidate>();
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var device in deviceRepo.GetAll()
                     .Where(d => !d.IsDeleted && !d.IsPermanentlyRemoved && d.Status == "online"))
        {
            var probePorts = new[] { 8443, 8080 }.Where(p => device.OpenPorts?.Contains(p) == true).ToList();
            var inventoryHint = OpenAvcHostProbe.LooksLikeOpenAvcTitle(device.HttpTitle)
                || string.Equals(device.Vendor, "OpenAVC", StringComparison.OrdinalIgnoreCase);

            if (probePorts.Count == 0 && !inventoryHint)
                continue;

            if (probePorts.Count == 0)
                probePorts.Add(8080);

            foreach (var port in probePorts)
            {
                var scheme = port == 8443 ? "https" : "http";
                var fallbackUrl = $"{scheme}://{device.IpAddress}:{port}";
                if (!seenUrls.Add(fallbackUrl))
                    continue;

                OpenAvcHostProbe.Match? live = null;
                if (device.OpenPorts?.Contains(port) == true)
                    live = await OpenAvcHostProbe.ProbeAsync(device.IpAddress, port, cancellationToken);

                if (live == null && !inventoryHint)
                    continue;

                candidates.Add(new OpenAvcNetworkCandidate
                {
                    DeviceId = device.Id,
                    IpAddress = device.IpAddress,
                    Hostname = device.Hostname,
                    BaseUrl = live?.BaseUrl ?? fallbackUrl,
                    Version = live?.Version == "unknown" ? null : live?.Version,
                    Source = live != null ? "probe" : "inventory",
                });
                break;
            }
        }

        return Ok(candidates.OrderBy(c => c.IpAddress, StringComparer.OrdinalIgnoreCase));
    }

    [HttpGet("settings")]
    [Authorize(Roles = "Admin")]
    public IActionResult GetSettings()
    {
        if (RejectIfPortable() is { } portable)
            return portable;

        if (IsHubBrain())
            return HubOnlyError();

        var settings = openAvcService.GetSettings();
        return Ok(new
        {
            settings.Enabled,
            settings.BaseUrl,
            settings.Username,
            Password = string.IsNullOrEmpty(settings.Password) ? "" : "********",
            settings.LastUpdatedUtc,
        });
    }

    [HttpPut("settings")]
    [Authorize(Roles = "Admin")]
    public IActionResult UpdateSettings([FromBody] OpenAvcSettingsUpdateRequest request)
    {
        if (RejectIfPortable() is { } portable)
            return portable;

        if (IsHubBrain())
            return HubOnlyError();

        if (string.IsNullOrWhiteSpace(request.BaseUrl))
            return BadRequest(new { message = "OpenAVC base URL is required." });

        if (!OpenAvcService.IsAllowedBaseUrl(request.BaseUrl, out var urlError))
            return BadRequest(new { message = urlError });

        if (string.IsNullOrWhiteSpace(request.Username))
            return BadRequest(new { message = "OpenAVC username is required." });

        var settings = openAvcService.GetSettings();
        var hadPassword = !string.IsNullOrEmpty(settings.Password);
        settings.Enabled = request.Enabled;
        settings.BaseUrl = request.BaseUrl.Trim().TrimEnd('/');
        settings.Username = request.Username.Trim();
        if (!string.IsNullOrEmpty(request.Password) && request.Password != "********")
            settings.Password = request.Password;

        if (settings.Enabled && string.IsNullOrEmpty(settings.Password))
            return BadRequest(new { message = "OpenAVC password is required when integration is enabled." });

        if (settings.Enabled && !hadPassword && (string.IsNullOrEmpty(request.Password) || request.Password == "********"))
            return BadRequest(new { message = "Enter your OpenAVC password before enabling the integration." });

        openAvcService.SaveSettings(settings);
        logger.LogInformation("OpenAVC settings updated by {User}", User.Identity?.Name ?? "unknown");
        _audit.Record(
            AuditActions.SettingsOpenAvcUpdate,
            "settings",
            "openavc",
            AuditOutcomes.Success,
            detail: $"enabled={settings.Enabled}; baseUrl={settings.BaseUrl}");
        return Ok(new { message = "OpenAVC settings saved." });
    }

    [HttpPost("test")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> TestConnection(CancellationToken cancellationToken)
    {
        if (RejectIfPortable() is { } portable)
            return portable;

        if (IsHubBrain())
            return HubOnlyError();

        var result = await openAvcService.TestConnectionAsync(cancellationToken);
        if (!result.Success)
            return BadRequest(new { message = result.Message, success = false });

        return Ok(new { message = result.Message, success = true, version = result.Version });
    }

    [HttpGet("health")]
    public async Task<IActionResult> GetHealth(CancellationToken cancellationToken)
    {
        if (RejectIfPortable() is { } portable)
            return portable;

        if (IsHubBrain())
            return Ok(new { status = "hub_relay", configured = false, reachable = false, authValid = false });

        var health = await openAvcService.ProbeIntegrationHealthAsync(cancellationToken);
        return Ok(new
        {
            status = health.Status,
            configured = health.Configured,
            reachable = health.Reachable,
            authValid = health.AuthValid,
            message = health.Message,
        });
    }

    [HttpGet("links")]
    public async Task<IActionResult> GetAllLinks(CancellationToken cancellationToken)
    {
        if (RejectIfPortable() is { } portable)
            return portable;

        if (IsHubBrain())
        {
            if (Relay == null)
                return Ok(Array.Empty<OpenAvcLinkSummary>());
            var relayed = await Relay.GetAllLinkSummariesAsync(cancellationToken);
            return Ok(relayed);
        }

        return Ok(openAvcService.GetAllLinkSummaries());
    }

    [HttpGet("context/{deviceId:guid}")]
    public async Task<IActionResult> GetDrawerContext(
        Guid deviceId,
        [FromQuery] string? ip,
        [FromQuery] string? hostname,
        CancellationToken cancellationToken)
    {
        if (RejectIfPortable() is { } portable)
            return portable;

        if (IsHubBrain())
        {
            if (Relay == null)
                return BadRequest(new { message = "OpenAVC relay is not available." });

            var device = deviceRepo.GetById(deviceId);
            var targetIp = ip ?? device?.IpAddress;
            var (context, error) = await Relay.GetDrawerContextAsync(
                deviceId,
                targetIp,
                hostname ?? device?.Hostname,
                device?.MacAddress,
                cancellationToken);
            if (error != null && context == null)
                return BadRequest(new { message = error });
            if (context != null)
                context.HubRelay = true;

            return Ok(context ?? new OpenAvcDrawerContext
            {
                HubRelay = true,
                IntegrationStatus = "offline",
                Configured = false,
            });
        }

        var localDevice = deviceRepo.GetById(deviceId);
        var ctx = await openAvcService.GetDrawerContextAsync(
            deviceId,
            ip ?? localDevice?.IpAddress,
            hostname ?? localDevice?.Hostname,
            localDevice?.MacAddress,
            cancellationToken);
        return Ok(ctx);
    }

    [HttpGet("link/{deviceId:guid}")]
    public async Task<IActionResult> GetLink(Guid deviceId, CancellationToken cancellationToken)
    {
        if (RejectIfPortable() is { } portable)
            return portable;

        if (IsHubBrain())
        {
            var device = deviceRepo.GetById(deviceId);
            if (device == null)
                return NotFound(new { message = "Device not found." });

            if (Relay == null)
                return Ok(new { linked = false, hubMode = true });

            var (context, _) = await Relay.GetDrawerContextAsync(
                deviceId,
                device.IpAddress,
                device.Hostname,
                device.MacAddress,
                cancellationToken);
            return Ok(new
            {
                linked = context?.Linked ?? false,
                openAvcDeviceId = context?.OpenAvcDeviceId,
                driverName = context?.DriverName,
                driverId = context?.DriverId,
                hubMode = true,
                hubRelay = true,
            });
        }

        var settings = openAvcService.GetSettings();
        var link = openAvcService.GetLink(deviceId);
        return Ok(new
        {
            linked = link != null,
            openAvcDeviceId = link?.OpenAvcDeviceId,
            driverName = link?.OpenAvcDriverName,
            driverId = link?.OpenAvcDriverId,
            integrationEnabled = settings.Enabled,
            configured = settings.Enabled
                && !string.IsNullOrWhiteSpace(settings.BaseUrl)
                && !string.IsNullOrEmpty(settings.Password),
        });
    }

    [HttpPut("link/{deviceId:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> SaveLink(
        Guid deviceId,
        [FromBody] OpenAvcLinkRequest request,
        CancellationToken cancellationToken)
    {
        if (RejectIfPortable() is { } portable)
            return portable;

        if (string.IsNullOrWhiteSpace(request.OpenAvcDeviceId))
            return BadRequest(new { message = "OpenAVC device id is required (device id from OpenAVC Programmer)." });

        var stacksDevice = deviceRepo.GetById(deviceId);
        OpenAvcDeviceLink link;
        if (IsHubBrain())
        {
            if (Relay == null)
                return HubOnlyError();

            var (relayed, error) = await Relay.SaveLinkAsync(
                deviceId,
                request.OpenAvcDeviceId,
                User.Identity?.Name,
                cancellationToken);
            if (error != null)
                return BadRequest(new { message = error });
            link = relayed!;
        }
        else
        {
            link = await openAvcService.SaveLinkAsync(
                deviceId,
                request.OpenAvcDeviceId,
                User.Identity?.Name,
                stacksDevice?.MacAddress,
                stacksDevice?.IpAddress,
                cancellationToken);
        }

        logger.LogInformation(
            "OpenAVC link saved by {User}: StacksAtlas {DeviceId} → OpenAVC {OpenAvcDeviceId}",
            User.Identity?.Name ?? "unknown",
            deviceId,
            link.OpenAvcDeviceId);

        return Ok(new
        {
            linked = true,
            openAvcDeviceId = link.OpenAvcDeviceId,
            driverName = link.OpenAvcDriverName,
            driverId = link.OpenAvcDriverId,
        });
    }

    [HttpDelete("link/{deviceId:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteLink(Guid deviceId, CancellationToken cancellationToken)
    {
        if (RejectIfPortable() is { } portable)
            return portable;

        if (IsHubBrain())
        {
            if (Relay == null)
                return HubOnlyError();

            var (success, message) = await Relay.DeleteLinkAsync(deviceId, cancellationToken);
            if (!success)
                return BadRequest(new { message = message ?? "Failed to remove link." });
            return Ok(new { message = "OpenAVC link removed." });
        }

        openAvcService.RemoveLink(deviceId);
        return Ok(new { message = "OpenAVC link removed." });
    }

    [HttpPost("link/{deviceId:guid}/command")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> SendCommand(
        Guid deviceId,
        [FromBody] OpenAvcCommandRequest request,
        CancellationToken cancellationToken)
    {
        if (RejectIfPortable() is { } portable)
            return portable;

        if (string.IsNullOrWhiteSpace(request.Command))
            return BadRequest(new { message = "Command is required." });

        var stacksDevice = deviceRepo.GetById(deviceId);
        OpenAvcCommandResult result;
        if (IsHubBrain())
        {
            if (Relay == null)
                return HubOnlyError();

            var (success, message, relayResult) = await Relay.SendDeviceCommandAsync(
                deviceId,
                request.Command,
                request.Params,
                cancellationToken);
            result = new OpenAvcCommandResult { Success = success, Message = message, Result = relayResult };
        }
        else
        {
            result = await openAvcService.SendDeviceCommandAsync(
                deviceId,
                request.Command,
                request.Params,
                stacksDevice?.MacAddress,
                stacksDevice?.IpAddress,
                cancellationToken);
        }

        if (!result.Success)
            return BadRequest(new { message = result.Message, success = false });

        logger.LogInformation(
            "OpenAVC command {Command} executed by {User} for device {DeviceId}",
            request.Command,
            User.Identity?.Name ?? "unknown",
            deviceId);

        return Ok(new { message = result.Message, success = true, result = result.Result });
    }

    [HttpPost("macros/{macroId}/execute")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ExecuteMacro(
        string macroId,
        [FromQuery] Guid deviceId,
        CancellationToken cancellationToken)
    {
        if (RejectIfPortable() is { } portable)
            return portable;

        if (deviceId == Guid.Empty)
            return BadRequest(new { message = "deviceId query parameter is required." });

        var stacksDevice = deviceRepo.GetById(deviceId);
        OpenAvcCommandResult result;
        if (IsHubBrain())
        {
            if (Relay == null)
                return HubOnlyError();

            var (success, message, relayResult) = await Relay.ExecuteMacroAsync(deviceId, macroId, cancellationToken);
            result = new OpenAvcCommandResult { Success = success, Message = message, Result = relayResult };
        }
        else
        {
            result = await openAvcService.ExecuteMacroAsync(
                macroId,
                deviceId,
                stacksDevice?.MacAddress,
                stacksDevice?.IpAddress,
                cancellationToken);
        }

        if (!result.Success)
            return BadRequest(new { message = result.Message, success = false });

        return Ok(new { message = result.Message, success = true, result = result.Result });
    }

    [HttpPut("link/{deviceId:guid}/macros")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> SaveMacroPins(
        Guid deviceId,
        [FromBody] OpenAvcMacroPinsRequest request,
        CancellationToken cancellationToken)
    {
        if (RejectIfPortable() is { } portable)
            return portable;

        OpenAvcCommandResult result;
        if (IsHubBrain())
        {
            if (Relay == null)
                return HubOnlyError();

            var (success, message) = await Relay.SaveMacroPinsAsync(
                deviceId,
                request.MacroIds ?? [],
                cancellationToken);
            result = new OpenAvcCommandResult { Success = success, Message = message };
        }
        else
        {
            var stacksDevice = deviceRepo.GetById(deviceId);
            result = openAvcService.SaveMacroPins(
                deviceId,
                request.MacroIds ?? [],
                stacksDevice?.MacAddress,
                stacksDevice?.IpAddress);
        }

        if (!result.Success)
            return BadRequest(new { message = result.Message, success = false });

        logger.LogInformation(
            "OpenAVC macro pins updated by {User} for device {DeviceId} ({Count} pins)",
            User.Identity?.Name ?? "unknown",
            deviceId,
            request.MacroIds?.Count ?? 0);

        return Ok(new { message = result.Message, success = true });
    }

    private static bool IsHubBrain() => ExecutionState.IsHubBrainEnabled;

    private IActionResult HubOnlyError() =>
        BadRequest(new { message = "OpenAVC credentials are configured on Nodes only. Control relays to the site-local OpenAVC instance." });
}

public record OpenAvcSettingsUpdateRequest(bool Enabled, string BaseUrl, string Username, string? Password);
public record OpenAvcLinkRequest(string OpenAvcDeviceId);
public record OpenAvcCommandRequest(string Command, Dictionary<string, object>? Params = null);
public record OpenAvcMacroPinsRequest(List<string>? MacroIds);
