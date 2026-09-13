using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using StacksAtlas.Core.Database;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Services.Devices;

namespace StacksAtlas.Core.Services.Integrations;

public class OpenAvcConnectionResult
{
    public bool Success { get; init; }
    public string? Message { get; init; }
    public string? Version { get; init; }
}

public class OpenAvcCommandResult
{
    public bool Success { get; init; }
    public string? Message { get; init; }
    /// <summary>OpenAVC driver return value from POST …/command (e.g. serial string, parsed JSON).</summary>
    public object? Result { get; init; }
}

public class OpenAvcService(
    OpenAvcSettingsRepository settingsRepository,
    OpenAvcDeviceLinkRepository linkRepository,
    IHttpClientFactory httpClientFactory,
    Func<DeviceAssetEnrichmentService>? assetEnrichmentFactory,
    ILogger<OpenAvcService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public OpenAvcSettings GetSettings() => settingsRepository.GetSettings();

    public void SaveSettings(OpenAvcSettings settings)
    {
        var existing = settingsRepository.GetSettings();
        settingsRepository.SaveSettings(settings, existing.Password);
    }

    public OpenAvcDeviceLink? GetLink(Guid stacksAtlasDeviceId) =>
        linkRepository.GetByDeviceId(stacksAtlasDeviceId);

    public OpenAvcDeviceLink? ResolveLink(Guid stacksAtlasDeviceId, string? macAddress, string? ipAddress) =>
        linkRepository.ResolveLinkForDevice(stacksAtlasDeviceId, macAddress, ipAddress);

    public List<OpenAvcLinkSummary> GetAllLinkSummaries() =>
        linkRepository.GetAll()
            .Select(l => new OpenAvcLinkSummary
            {
                StacksAtlasDeviceId = l.StacksAtlasDeviceId,
                OpenAvcDeviceId = l.OpenAvcDeviceId,
                DriverName = l.OpenAvcDriverName,
                DriverId = l.OpenAvcDriverId,
            })
            .ToList();

    public async Task<OpenAvcDeviceLink> SaveLinkAsync(
        Guid stacksAtlasDeviceId,
        string openAvcDeviceId,
        string? linkedBy,
        string? deviceMac = null,
        string? deviceIp = null,
        CancellationToken cancellationToken = default)
    {
        var existingByMac = linkRepository.GetByMacAddress(deviceMac);
        if (existingByMac != null && existingByMac.StacksAtlasDeviceId != stacksAtlasDeviceId)
            linkRepository.Delete(existingByMac.StacksAtlasDeviceId);

        var existing = linkRepository.GetByDeviceId(stacksAtlasDeviceId) ?? existingByMac;

        var link = new OpenAvcDeviceLink
        {
            StacksAtlasDeviceId = stacksAtlasDeviceId,
            OpenAvcDeviceId = openAvcDeviceId.Trim(),
            LinkedAtUtc = DateTime.UtcNow,
            LinkedBy = linkedBy,
            StacksAtlasMacAddress = OpenAvcDeviceLinkRepository.NormalizeMac(deviceMac),
            StacksAtlasIp = string.IsNullOrWhiteSpace(deviceIp) ? null : deviceIp.Trim(),
            PinnedMacroIds = existing?.PinnedMacroIds ?? [],
            ReadingsCache = existing?.ReadingsCache ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        };

        try
        {
            var detail = await GetDeviceDetailRawAsync(openAvcDeviceId.Trim(), cancellationToken);
            if (detail != null)
            {
                link.OpenAvcDriverId = detail.DriverId;
                link.OpenAvcDriverName = detail.DriverName;
                link.OpenAvcDeviceName = detail.Name;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not enrich OpenAVC link metadata for {OpenAvcDeviceId}", openAvcDeviceId);
        }

        linkRepository.Upsert(link);
        return link;
    }

    public bool RemoveLink(Guid stacksAtlasDeviceId) => linkRepository.Delete(stacksAtlasDeviceId);

    public OpenAvcCommandResult SaveMacroPins(
        Guid stacksAtlasDeviceId,
        IReadOnlyList<string> macroIds,
        string? deviceMac = null,
        string? deviceIp = null)
    {
        var link = linkRepository.ResolveLinkForDevice(stacksAtlasDeviceId, deviceMac, deviceIp);
        if (link == null)
            return new OpenAvcCommandResult { Success = false, Message = "Device is not linked to OpenAVC." };

        link.PinnedMacroIds = macroIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        linkRepository.Upsert(link);

        return new OpenAvcCommandResult
        {
            Success = true,
            Message = link.PinnedMacroIds.Count > 0
                ? $"Pinned {link.PinnedMacroIds.Count} macro(s) to this device."
                : "Macro pins cleared. Full project catalog will show.",
        };
    }

    public async Task<OpenAvcConnectionResult> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        var health = await ProbeIntegrationHealthAsync(cancellationToken);
        if (!health.Configured)
            return new OpenAvcConnectionResult { Success = false, Message = health.Message ?? "OpenAVC is not configured." };
        if (!health.Reachable)
            return new OpenAvcConnectionResult { Success = false, Message = health.Message ?? "Cannot reach OpenAVC." };
        if (!health.AuthValid)
            return new OpenAvcConnectionResult { Success = false, Message = health.Message ?? "OpenAVC authentication failed." };

        var settings = GetValidatedSettings();
        var version = settings.value != null
            ? await TryGetVersionAsync(settings.value, cancellationToken)
            : null;
        return new OpenAvcConnectionResult
        {
            Success = true,
            Message = "Connected to OpenAVC.",
            Version = version,
        };
    }

    public async Task<(bool Configured, bool Reachable, bool AuthValid, string Status, string? Message)> ProbeIntegrationHealthAsync(
        CancellationToken cancellationToken = default)
    {
        var settings = settingsRepository.GetSettings();
        var configured = settings.Enabled
            && !string.IsNullOrWhiteSpace(settings.BaseUrl)
            && !string.IsNullOrWhiteSpace(settings.Username)
            && !string.IsNullOrEmpty(settings.Password);
        if (!configured)
            return (false, false, false, "disabled", "OpenAVC integration is disabled or incomplete.");

        var validated = GetValidatedSettings();
        if (validated.error != null)
            return (true, false, false, "disabled", validated.error);

        try
        {
            // Lightweight auth probe: avoid GET /api/devices (full catalog) on every UI health poll.
            using var request = CreateRequest(HttpMethod.Get, validated.value!, "/api/project");
            using var probeTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            probeTimeout.CancelAfter(TimeSpan.FromSeconds(6));
            using var response = await SendAsync(request, probeTimeout.Token);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return (true, true, false, "auth_failed",
                    "OpenAVC is reachable but credentials were rejected. Re-save in Settings → OpenAVC.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return (true, true, false, "auth_failed",
                    $"OpenAVC returned HTTP {(int)response.StatusCode}.");
            }

            return (true, true, true, "online", null);
        }
        catch (HttpRequestException ex)
        {
            logger.LogDebug(ex, "OpenAVC health probe failed for {BaseUrl}", settings.BaseUrl);
            return (true, false, false, "offline",
                "Cannot reach OpenAVC. Verify the service is running and the base URL is correct.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (true, false, false, "offline", "OpenAVC did not respond in time.");
        }
    }

    public async Task<OpenAvcDrawerContext> GetDrawerContextAsync(
        Guid stacksAtlasDeviceId,
        string? targetIp,
        string? deviceHostname = null,
        string? deviceMac = null,
        CancellationToken cancellationToken = default)
    {
        var settings = settingsRepository.GetSettings();
        var context = new OpenAvcDrawerContext
        {
            IntegrationEnabled = settings.Enabled,
            Configured = settings.Enabled
                && !string.IsNullOrWhiteSpace(settings.BaseUrl)
                && !string.IsNullOrWhiteSpace(settings.Username)
                && !string.IsNullOrEmpty(settings.Password),
        };

        var link = linkRepository.ResolveLinkForDevice(stacksAtlasDeviceId, deviceMac, targetIp);
        if (link != null)
        {
            context.Linked = true;
            context.OpenAvcDeviceId = link.OpenAvcDeviceId;
            context.DriverId = link.OpenAvcDriverId;
            context.DriverName = link.OpenAvcDriverName;
            context.DeviceName = link.OpenAvcDeviceName;
            context.PinnedMacroIds = link.PinnedMacroIds ?? [];
            context.HasMacroPins = context.PinnedMacroIds.Count > 0;
        }

        if (!context.Configured)
        {
            context.IntegrationStatus = "disabled";
            return context;
        }

        var health = await ProbeIntegrationHealthAsync(cancellationToken);
        context.OpenAvcReachable = health.Reachable;
        context.OpenAvcAuthValid = health.AuthValid;
        context.IntegrationStatus = health.Status;

        if (!health.Reachable || !health.AuthValid)
            return context;

        try
        {
            if (!context.Linked)
            {
                context.AvailableDevices = await ListDevicesEnrichedAsync(cancellationToken);
            }

            if (!string.IsNullOrWhiteSpace(targetIp) && !context.Linked)
            {
                context.Suggestion = await SuggestDeviceByIpAsync(
                    targetIp,
                    deviceHostname,
                    context.AvailableDevices,
                    cancellationToken);
            }

            if (link != null)
            {
                var detail = await GetDeviceDetailRawAsync(link.OpenAvcDeviceId, cancellationToken);
                if (detail != null)
                {
                    context.Commands = detail.Commands;
                    context.State = detail.State;
                    context.DisplayState = FilterDisplayState(detail.State);
                    ApplyStatusFields(context, detail.State, link.ReadingsCache);
                    context.DriverId ??= detail.DriverId;
                    context.DriverName ??= detail.DriverName;
                    context.DeviceName ??= detail.Name;
                    await TryEnrichLinkedAssetsAsync(
                        stacksAtlasDeviceId,
                        deviceMac,
                        targetIp,
                        detail.State,
                        cancellationToken);
                }
            }

            var allMacros = await ListMacrosAsync(cancellationToken);
            context.AllMacros = allMacros;
            context.Macros = ApplyMacroPins(allMacros, link?.PinnedMacroIds ?? []);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "OpenAVC drawer context partial failure for device {DeviceId}", stacksAtlasDeviceId);
        }

        return context;
    }

    public async Task<OpenAvcLinkSuggestion?> SuggestDeviceByIpAsync(
        string targetIp,
        string? deviceHostname = null,
        List<OpenAvcDeviceInfo>? prefetchedDevices = null,
        CancellationToken cancellationToken = default)
    {
        var settings = GetValidatedSettings();
        if (settings.error != null)
            return null;

        var devices = prefetchedDevices ?? await ListDevicesEnrichedAsync(cancellationToken);
        var normalized = NormalizeIp(targetIp);
        var match = devices.FirstOrDefault(d => NormalizeIp(d.Ip) == normalized);
        if (match == null && !string.IsNullOrWhiteSpace(deviceHostname))
        {
            var hostKey = NormalizeMatchKey(deviceHostname);
            match = devices.FirstOrDefault(d =>
                NormalizeMatchKey(d.Name) == hostKey
                || NormalizeMatchKey(d.Id) == hostKey);
        }

        if (match == null)
            return null;

        return new OpenAvcLinkSuggestion
        {
            OpenAvcDeviceId = match.Id,
            Name = match.Name,
            Ip = match.Ip,
            DriverId = match.DriverId,
            DriverName = match.DriverName,
            MatchReason = NormalizeIp(match.Ip) == normalized ? "ip" : "hostname",
        };
    }

    public async Task<List<OpenAvcDeviceInfo>> ListCatalogDevicesAsync(CancellationToken cancellationToken = default) =>
        await ListDevicesEnrichedAsync(cancellationToken);

    public async Task<OpenAvcCommandResult> SendDeviceCommandAsync(
        Guid stacksAtlasDeviceId,
        string command,
        Dictionary<string, object>? parameters = null,
        string? deviceMac = null,
        string? deviceIp = null,
        CancellationToken cancellationToken = default)
    {
        var settings = GetValidatedSettings();
        if (settings.error != null)
            return new OpenAvcCommandResult { Success = false, Message = settings.error };

        var link = linkRepository.ResolveLinkForDevice(stacksAtlasDeviceId, deviceMac, deviceIp);
        if (link == null || string.IsNullOrWhiteSpace(link.OpenAvcDeviceId))
            return new OpenAvcCommandResult { Success = false, Message = "Device is not linked to OpenAVC." };

        if (string.IsNullOrWhiteSpace(command))
            return new OpenAvcCommandResult { Success = false, Message = "Command is required." };

        return await SendOpenAvcDeviceCommandAsync(
            link.OpenAvcDeviceId,
            command,
            parameters,
            stacksAtlasDeviceId,
            deviceMac,
            deviceIp,
            cancellationToken);
    }

    public async Task<OpenAvcCommandResult> ExecuteMacroAsync(
        string macroId,
        Guid? stacksAtlasDeviceId = null,
        string? deviceMac = null,
        string? deviceIp = null,
        CancellationToken cancellationToken = default)
    {
        var settings = GetValidatedSettings();
        if (settings.error != null)
            return new OpenAvcCommandResult { Success = false, Message = settings.error };

        if (string.IsNullOrWhiteSpace(macroId))
            return new OpenAvcCommandResult { Success = false, Message = "Macro id is required." };

        if (stacksAtlasDeviceId.HasValue)
        {
            var link = linkRepository.ResolveLinkForDevice(stacksAtlasDeviceId.Value, deviceMac, deviceIp);
            if (link == null || string.IsNullOrWhiteSpace(link.OpenAvcDeviceId))
            {
                return new OpenAvcCommandResult
                {
                    Success = false,
                    Message = "Device is not linked to OpenAVC.",
                };
            }

            if (link.PinnedMacroIds is { Count: > 0 } pins
                && !pins.Contains(macroId, StringComparer.OrdinalIgnoreCase))
            {
                return new OpenAvcCommandResult
                {
                    Success = false,
                    Message = "This macro is not pinned to this device. Add it under Pinned macros first.",
                };
            }
        }

        try
        {
            using var request = CreateRequest(
                HttpMethod.Post,
                settings.value!,
                $"/api/macros/{Uri.EscapeDataString(macroId)}/execute");
            using var response = await SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new OpenAvcCommandResult
                {
                    Success = false,
                    Message = response.StatusCode == HttpStatusCode.Unauthorized
                        ? "OpenAVC authentication failed. Re-save credentials in Settings → OpenAVC."
                        : TryReadDetail(body) ?? $"OpenAVC returned HTTP {(int)response.StatusCode}.",
                };
            }

            var (result, summary, status) = ParseOpenAvcResponseBody(body);
            var message = summary != null
                ? $"Macro \"{macroId}\" completed: {summary}"
                : status == "executed"
                    ? $"Macro \"{macroId}\" completed."
                    : $"Macro \"{macroId}\" sent to OpenAVC.";

            if (stacksAtlasDeviceId.HasValue)
            {
                await CaptureActionReadingAsync(
                    stacksAtlasDeviceId.Value,
                    deviceMac,
                    deviceIp,
                    macroId,
                    null,
                    result,
                    summary,
                    cancellationToken);
            }

            return new OpenAvcCommandResult
            {
                Success = true,
                Result = result,
                Message = message,
            };
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "OpenAVC macro transport failed for {MacroId}", macroId);
            return new OpenAvcCommandResult { Success = false, Message = "Cannot reach OpenAVC on this node." };
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("OpenAVC macro HTTP wait exceeded for {MacroId}  -  macro may still be running on OpenAVC", macroId);
            return new OpenAvcCommandResult
            {
                Success = false,
                Message = "OpenAVC took too long to respond. The macro may still be running. Check the device or shorten waits in OpenAVC.",
            };
        }
        catch (TaskCanceledException)
        {
            return new OpenAvcCommandResult { Success = false, Message = "Macro request cancelled." };
        }
    }

    internal async Task<OpenAvcCommandResult> SendOpenAvcDeviceCommandAsync(
        string openAvcDeviceId,
        string command,
        Dictionary<string, object>? parameters,
        Guid? stacksAtlasDeviceId = null,
        string? deviceMac = null,
        string? deviceIp = null,
        CancellationToken cancellationToken = default)
    {
        var settings = GetValidatedSettings();
        if (settings.error != null)
            return new OpenAvcCommandResult { Success = false, Message = settings.error };

        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                command,
                @params = parameters ?? new Dictionary<string, object>(),
            });
            using var request = CreateRequest(
                HttpMethod.Post,
                settings.value!,
                $"/api/devices/{Uri.EscapeDataString(openAvcDeviceId)}/command",
                payload);
            using var response = await SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "OpenAVC command {Command} failed for device {OpenAvcDeviceId} with status {StatusCode}",
                    command,
                    openAvcDeviceId,
                    (int)response.StatusCode);

                return new OpenAvcCommandResult
                {
                    Success = false,
                    Message = response.StatusCode == HttpStatusCode.Unauthorized
                        ? "OpenAVC authentication failed. Re-save credentials in Settings → OpenAVC."
                        : TryReadDetail(body) ?? $"OpenAVC returned HTTP {(int)response.StatusCode}.",
                };
            }

            var (result, summary) = ParseCommandResult(body);
            if (stacksAtlasDeviceId.HasValue)
            {
                await CaptureActionReadingAsync(
                    stacksAtlasDeviceId.Value,
                    deviceMac,
                    deviceIp,
                    command,
                    null,
                    result,
                    summary,
                    cancellationToken);
            }

            return new OpenAvcCommandResult
            {
                Success = true,
                Result = result,
                Message = summary != null
                    ? $"Command completed: {summary}"
                    : "Command completed.",
            };
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "OpenAVC command transport failed for {OpenAvcDeviceId}", openAvcDeviceId);
            return new OpenAvcCommandResult { Success = false, Message = "Cannot reach OpenAVC on this node." };
        }
        catch (TaskCanceledException)
        {
            return new OpenAvcCommandResult { Success = false, Message = "OpenAVC command timed out." };
        }
    }

    private async Task<List<OpenAvcDeviceInfo>> ListDevicesEnrichedAsync(CancellationToken cancellationToken)
    {
        var settings = GetValidatedSettings();
        if (settings.error != null)
            return [];

        using var devicesRequest = CreateRequest(HttpMethod.Get, settings.value!, "/api/devices");
        using var devicesResponse = await SendAsync(devicesRequest, cancellationToken);
        var devicesBody = await devicesResponse.Content.ReadAsStringAsync(cancellationToken);
        if (!devicesResponse.IsSuccessStatusCode)
            return [];

        var devices = ParseDeviceList(devicesBody);
        var connections = await GetConnectionsMapAsync(settings.value!, cancellationToken);

        foreach (var device in devices)
        {
            if (!string.IsNullOrWhiteSpace(device.Ip))
                continue;

            if (connections.TryGetValue(device.Id, out var connHost) && !string.IsNullOrWhiteSpace(connHost))
            {
                device.Ip = connHost;
                continue;
            }

            var detail = await GetDeviceDetailRawAsync(device.Id, cancellationToken);
            if (detail != null && !string.IsNullOrWhiteSpace(detail.Ip))
                device.Ip = detail.Ip;
        }

        return devices;
    }

    private async Task<Dictionary<string, string>> GetConnectionsMapAsync(
        OpenAvcSettings settings,
        CancellationToken cancellationToken)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var request = CreateRequest(HttpMethod.Get, settings, "/api/connections");
            using var response = await SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                return map;

            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return map;

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Object)
                    continue;
                var host = ReadString(prop.Value, "host")
                    ?? ReadString(prop.Value, "ip")
                    ?? ReadString(prop.Value, "address");
                if (!string.IsNullOrWhiteSpace(host))
                    map[prop.Name] = host;
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "OpenAVC connections map unavailable  -  IP suggest may be partial");
        }

        return map;
    }

    private async Task<OpenAvcDeviceDetail?> GetDeviceDetailRawAsync(string openAvcDeviceId, CancellationToken cancellationToken)
    {
        var settings = GetValidatedSettings();
        if (settings.error != null)
            return null;

        using var request = CreateRequest(
            HttpMethod.Get,
            settings.value!,
            $"/api/devices/{Uri.EscapeDataString(openAvcDeviceId)}");
        using var response = await SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        return ParseDeviceDetail(body);
    }

    private async Task<List<OpenAvcMacroInfo>> ListMacrosAsync(CancellationToken cancellationToken)
    {
        var settings = GetValidatedSettings();
        if (settings.error != null)
            return [];

        using var request = CreateRequest(HttpMethod.Get, settings.value!, "/api/project");
        using var response = await SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            return [];

        return ParseMacros(body);
    }

    private async Task<string?> TryGetVersionAsync(OpenAvcSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            using var healthRequest = CreateRequest(HttpMethod.Get, settings, "/api/health");
            using var healthResponse = await SendAsync(healthRequest, cancellationToken);
            if (!healthResponse.IsSuccessStatusCode)
                return null;
            var healthBody = await healthResponse.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(healthBody);
            return doc.RootElement.TryGetProperty("version", out var versionProp) ? versionProp.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    public static bool IsAllowedBaseUrl(string baseUrl, out string? error)
    {
        error = null;
        if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var uri))
        {
            error = "OpenAVC base URL must be a valid absolute URL.";
            return false;
        }

        if (uri.Scheme is not "http" and not "https")
        {
            error = "OpenAVC base URL must use http or https.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(uri.Host))
        {
            error = "OpenAVC base URL must include a host.";
            return false;
        }

        var host = uri.Host.ToLowerInvariant();
        if (host is "169.254.169.254" or "metadata.google.internal")
        {
            error = "OpenAVC base URL host is not allowed.";
            return false;
        }

        return true;
    }

    private (OpenAvcSettings? value, string? error) GetValidatedSettings()
    {
        var settings = settingsRepository.GetSettings();
        if (!settings.Enabled)
            return (null, "OpenAVC integration is disabled.");
        if (string.IsNullOrWhiteSpace(settings.BaseUrl))
            return (null, "OpenAVC base URL is not configured.");
        if (!IsAllowedBaseUrl(settings.BaseUrl, out var urlError))
            return (null, urlError);
        if (string.IsNullOrWhiteSpace(settings.Username))
            return (null, "OpenAVC username is not configured.");
        if (string.IsNullOrEmpty(settings.Password))
            return (null, "OpenAVC password is not configured. Re-save credentials in Settings → OpenAVC.");
        return (settings, null);
    }

    private static List<OpenAvcDeviceInfo> ParseDeviceList(string json)
    {
        var results = new List<OpenAvcDeviceInfo>();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
                results.Add(ParseDeviceInfo(item));
            return results;
        }

        if (root.ValueKind == JsonValueKind.Object)
        {
            if (root.TryGetProperty("devices", out var devices) && devices.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in devices.EnumerateArray())
                    results.Add(ParseDeviceInfo(item));
                return results;
            }

            foreach (var prop in root.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.Object)
                {
                    var info = ParseDeviceInfo(prop.Value);
                    if (string.IsNullOrWhiteSpace(info.Id))
                        info.Id = prop.Name;
                    results.Add(info);
                }
            }
        }

        return results;
    }

    private static OpenAvcDeviceInfo ParseDeviceInfo(JsonElement item)
    {
        return new OpenAvcDeviceInfo
        {
            Id = ReadString(item, "id") ?? ReadString(item, "device_id") ?? "",
            Name = ReadString(item, "name") ?? ReadString(item, "label"),
            Ip = ReadDeviceIp(item),
            DriverId = ReadString(item, "driver") ?? ReadString(item, "driver_id"),
            DriverName = ReadString(item, "driver_name") ?? ReadString(item, "driver_id"),
            Connected = ReadBool(item, "connected") ?? ReadBool(item, "is_connected"),
        };
    }

    private static string FormatStateValue(JsonElement value) =>
        value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Array or JsonValueKind.Object => value.GetRawText(),
            _ => value.ToString(),
        };

    private static OpenAvcDeviceDetail? ParseDeviceDetail(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var info = ParseDeviceInfo(root);
        var detail = new OpenAvcDeviceDetail
        {
            Id = info.Id,
            Name = info.Name,
            Ip = info.Ip,
            DriverId = info.DriverId,
            DriverName = info.DriverName,
        };

        if (root.TryGetProperty("commands", out var commands) && commands.ValueKind == JsonValueKind.Array)
        {
            foreach (var cmd in commands.EnumerateArray())
            {
                var id = ReadString(cmd, "id") ?? ReadString(cmd, "command") ?? ReadString(cmd, "name");
                if (string.IsNullOrWhiteSpace(id))
                    continue;
                detail.Commands.Add(new OpenAvcCommandInfo
                {
                    Id = id,
                    Label = ReadString(cmd, "label") ?? id.Replace('_', ' '),
                });
            }
        }
        else if (root.TryGetProperty("available_commands", out var available) && available.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in available.EnumerateObject())
            {
                var label = prop.Value.ValueKind == JsonValueKind.Object
                    ? ReadString(prop.Value, "label") ?? prop.Name.Replace('_', ' ')
                    : prop.Name.Replace('_', ' ');
                detail.Commands.Add(new OpenAvcCommandInfo { Id = prop.Name, Label = label });
            }
        }

        if (root.TryGetProperty("state", out var state) && state.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in state.EnumerateObject())
            {
                detail.State[prop.Name] = FormatStateValue(prop.Value);
            }
        }

        return detail;
    }

    private static List<OpenAvcMacroInfo> ParseMacros(string json)
    {
        var macros = new List<OpenAvcMacroInfo>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("macros", out var macrosEl) || macrosEl.ValueKind != JsonValueKind.Array)
            return macros;

        foreach (var item in macrosEl.EnumerateArray())
        {
            var id = ReadString(item, "id") ?? ReadString(item, "macro_id");
            if (string.IsNullOrWhiteSpace(id))
                continue;
            macros.Add(new OpenAvcMacroInfo
            {
                Id = id,
                Name = ReadString(item, "name") ?? ReadString(item, "label") ?? id,
            });
        }
        return macros;
    }

    private static string? ReadDeviceIp(JsonElement item)
    {
        var direct = ReadString(item, "ip")
            ?? ReadString(item, "ip_address")
            ?? ReadString(item, "ipAddress")
            ?? ReadString(item, "host")
            ?? ReadString(item, "address");
        if (!string.IsNullOrWhiteSpace(direct))
            return StripUrlHost(direct);

        if (item.TryGetProperty("config", out var config) && config.ValueKind == JsonValueKind.Object)
        {
            var fromConfig = ReadString(config, "ip")
                ?? ReadString(config, "host")
                ?? ReadString(config, "address")
                ?? ReadString(config, "base_url")
                ?? ReadString(config, "url");
            if (!string.IsNullOrWhiteSpace(fromConfig))
                return StripUrlHost(fromConfig);
        }

        if (item.TryGetProperty("connection", out var connection) && connection.ValueKind == JsonValueKind.Object)
        {
            var fromConn = ReadString(connection, "host") ?? ReadString(connection, "ip");
            if (!string.IsNullOrWhiteSpace(fromConn))
                return StripUrlHost(fromConn);
        }

        return null;
    }

    private static string? StripUrlHost(string value)
    {
        var trimmed = value.Trim();
        if (!trimmed.Contains("://", StringComparison.Ordinal))
            return trimmed;
        return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ? uri.Host : trimmed;
    }

    private static string NormalizeMatchKey(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "" : value.Trim().ToLowerInvariant();

    private static List<OpenAvcMacroInfo> ApplyMacroPins(
        List<OpenAvcMacroInfo> allMacros,
        List<string> pinnedIds)
    {
        if (pinnedIds.Count == 0)
            return allMacros;

        var byId = allMacros.ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);
        var pinned = new List<OpenAvcMacroInfo>();
        foreach (var id in pinnedIds)
        {
            if (byId.TryGetValue(id, out var macro))
                pinned.Add(macro);
        }

        return pinned;
    }

    private static readonly string[] PreferredStateKeys =
    [
        "connected", "online", "power", "input", "source",
    ];

    private static readonly (string[] Keys, string Label)[] ReadingSources =
    [
        (["serial", "serial_number", "serialNumber"], "Serial"),
        (["firmware", "firmware_version", "firmwareVersion", "version"], "Firmware"),
        (["model", "model_number", "modelNumber"], "Model"),
        (["asset_tag", "assetTag"], "Asset tag"),
        (["installed_apps", "installedApps", "app_list", "apps_list"], "Installed apps"),
    ];

    private static readonly string[] PriorityStateReadingKeys =
    [
        "last_raw_response", "lastRawResponse",
        "last_message", "lastMessage", "message",
        "last_output", "lastOutput", "output",
    ];

    private static Dictionary<string, string> FilterDisplayState(Dictionary<string, string> state)
    {
        if (state.Count == 0)
            return state;

        var filtered = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in PreferredStateKeys)
        {
            if (state.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                filtered[key] = value.Trim();
        }

        return filtered;
    }

    private static string? ReadString(JsonElement item, string property) =>
        item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool? ReadBool(JsonElement item, string property) =>
        item.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    private static string NormalizeIp(string? ip) =>
        string.IsNullOrWhiteSpace(ip) ? "" : ip.Trim().ToLowerInvariant();

    private HttpRequestMessage CreateRequest(HttpMethod method, OpenAvcSettings settings, string path, string? jsonBody = null)
    {
        var url = $"{settings.BaseUrl.TrimEnd('/')}{path}";
        var request = new HttpRequestMessage(method, url);
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.Username}:{settings.Password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        if (jsonBody != null)
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        return request;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(nameof(OpenAvcService));
        return await client.SendAsync(request, cancellationToken);
    }

    private static string? TryReadDetail(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("detail", out var detail))
            {
                return detail.ValueKind == JsonValueKind.String
                    ? detail.GetString()
                    : detail.ToString();
            }
        }
        catch
        {
            // ignore
        }
        return null;
    }

    private static (object? Result, string? Summary) ParseCommandResult(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("result", out var resultEl)
                || resultEl.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return (null, null);
            }

            var result = JsonSerializer.Deserialize<object>(resultEl.GetRawText());
            var summary = FormatResultSummary(resultEl);
            return (result, summary);
        }
        catch
        {
            return (null, null);
        }
    }

    private static string? FormatResultSummary(JsonElement resultEl) =>
        resultEl.ValueKind switch
        {
            JsonValueKind.String => resultEl.GetString(),
            JsonValueKind.Number => resultEl.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Object or JsonValueKind.Array => resultEl.GetRawText(),
            _ => null,
        };

    private static void ApplyStatusFields(
        OpenAvcDrawerContext context,
        Dictionary<string, string> state,
        Dictionary<string, string>? readingsCache = null)
    {
        context.LastRawResponse = TryReadStateValue(state, "last_raw_response", "lastRawResponse");
        context.LastMessage = TryReadStateValue(state, "last_message", "lastMessage", "message")
            ?? context.LastRawResponse;
        context.LastError = TryReadStateValue(state, "last_error", "lastError", "error");
        context.ParsedSerial = TryReadStateValue(state, "serial", "serial_number", "serialNumber");
        context.Readings = BuildReadings(state, context.LastRawResponse, readingsCache);
    }

    private async Task CaptureActionReadingAsync(
        Guid stacksAtlasDeviceId,
        string? deviceMac,
        string? deviceIp,
        string actionId,
        string? actionName,
        object? httpResult,
        string? httpSummary,
        CancellationToken cancellationToken)
    {
        var link = linkRepository.ResolveLinkForDevice(stacksAtlasDeviceId, deviceMac, deviceIp);
        if (link == null || string.IsNullOrWhiteSpace(link.OpenAvcDeviceId))
            return;

        string? value = null;
        try
        {
            value = await TryExtractReadingAfterActionAsync(
                link.OpenAvcDeviceId,
                httpResult,
                httpSummary,
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not refresh OpenAVC state after action {ActionId}", actionId);
        }

        value ??= ExtractReadingValue(null, httpResult, httpSummary);
        if (string.IsNullOrWhiteSpace(value))
            return;

        value = value.Trim();
        string? resolvedName = actionName;
        if (string.IsNullOrWhiteSpace(resolvedName))
        {
            try
            {
                var macros = await ListMacrosAsync(cancellationToken);
                resolvedName = macros.FirstOrDefault(m =>
                    string.Equals(m.Id, actionId, StringComparison.OrdinalIgnoreCase))?.Name;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Could not resolve macro name for {ActionId}", actionId);
            }
        }

        var label = InferReadingLabel(actionId, resolvedName);
        link.ReadingsCache ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        link.ReadingsCache[label] = value;
        linkRepository.Upsert(link);
        await TryEnrichLinkedAssetsAsync(stacksAtlasDeviceId, deviceMac, deviceIp, null, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, string>?> GetOpenAvcDeviceStateAsync(
        string openAvcDeviceId,
        CancellationToken cancellationToken = default)
    {
        var detail = await GetDeviceDetailRawAsync(openAvcDeviceId, cancellationToken);
        return detail?.State;
    }

    private async Task TryEnrichLinkedAssetsAsync(
        Guid stacksAtlasDeviceId,
        string? deviceMac,
        string? deviceIp,
        IReadOnlyDictionary<string, string>? prefetchedState,
        CancellationToken cancellationToken)
    {
        if (assetEnrichmentFactory == null)
            return;

        try
        {
            await assetEnrichmentFactory().EnrichLinkedDeviceAsync(
                stacksAtlasDeviceId,
                deviceMac,
                deviceIp,
                prefetchedState,
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "OpenAVC asset enrichment skipped for device {DeviceId}", stacksAtlasDeviceId);
        }
    }

    private async Task<string?> TryExtractReadingAfterActionAsync(
        string openAvcDeviceId,
        object? httpResult,
        string? httpSummary,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (attempt > 0)
                await Task.Delay(450, cancellationToken);

            var detail = await GetDeviceDetailRawAsync(openAvcDeviceId, cancellationToken);
            var value = ExtractReadingValue(detail?.State, httpResult, attempt == 0 ? httpSummary : null);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    private static string? ExtractReadingValue(
        Dictionary<string, string>? state,
        object? httpResult,
        string? httpSummary)
    {
        var candidates = new List<string>();

        void AddCandidate(string? raw)
        {
            var normalized = NormalizeReadingText(raw);
            if (!string.IsNullOrWhiteSpace(normalized) && !IsIgnorableReading(normalized))
                candidates.Add(normalized);
        }

        if (state != null)
        {
            foreach (var key in PriorityStateReadingKeys)
            {
                if (state.TryGetValue(key, out var value))
                    AddCandidate(value);
            }

            foreach (var kvp in state.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (IsDynamicReadingStateKey(kvp.Key))
                    AddCandidate(kvp.Value);
            }
        }

        AddCandidate(FormatMacroResultValue(httpResult));
        AddCandidate(httpSummary);

        return candidates.OrderByDescending(c => c.Length).FirstOrDefault();
    }

    private static bool IsDynamicReadingStateKey(string key)
    {
        var lower = key.ToLowerInvariant();
        return lower.Contains("installed_app")
               || lower.Contains("app_list")
               || lower.Contains("apps_list")
               || (lower.Contains("app") && !lower.Contains("apple"));
    }

    private static string NormalizeReadingText(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var trimmed = raw.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
            trimmed = trimmed[1..^1];

        trimmed = trimmed.Replace("\\n", "\n", StringComparison.Ordinal);
        var parsed = TryParseOkValue(trimmed);
        return parsed ?? trimmed;
    }

    private static bool IsIgnorableReading(string value) =>
        value.Length < 2
        || value.Equals("ok", StringComparison.OrdinalIgnoreCase)
        || value.Equals("executed", StringComparison.OrdinalIgnoreCase)
        || value.Equals("success", StringComparison.OrdinalIgnoreCase);

    private static string? FormatMacroResultValue(object? result)
    {
        if (result == null)
            return null;
        if (result is string s)
            return TryParseOkValue(s) ?? (string.IsNullOrWhiteSpace(s) ? null : s.Trim());
        if (result is JsonElement el)
        {
            return el.ValueKind switch
            {
                JsonValueKind.String => TryParseOkValue(el.GetString()) ?? el.GetString()?.Trim(),
                JsonValueKind.Number => el.ToString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => el.GetRawText(),
            };
        }

        return result.ToString()?.Trim();
    }

    private static string InferReadingLabel(string macroId, string? macroName)
    {
        var haystack = $"{macroId} {macroName}".ToLowerInvariant();
        if (haystack.Contains("firmware") || haystack.Contains("fw_ver") || haystack.Contains("_fw"))
            return "Firmware";
        if (haystack.Contains("serial"))
            return "Serial";
        if (haystack.Contains("model"))
            return "Model";
        if (haystack.Contains("asset"))
            return "Asset tag";
        if (haystack.Contains("app") && !haystack.Contains("apple"))
            return "Installed apps";
        if (haystack.Contains("version") && !haystack.Contains("conversion"))
            return "Version";

        var name = macroName?.Trim();
        if (!string.IsNullOrWhiteSpace(name) && !string.Equals(name, macroId, StringComparison.OrdinalIgnoreCase))
            return name.Length > 56 ? $"{name[..53]}..." : name;

        return HumanizeKey(macroId);
    }

    private static string HumanizeKey(string key) =>
        key.Replace('_', ' ').Replace('-', ' ').Trim();

    private static List<OpenAvcReading> BuildReadings(
        Dictionary<string, string> state,
        string? lastRaw,
        Dictionary<string, string>? cachedReadings)
    {
        var readings = new List<OpenAvcReading>();
        var seenValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (cachedReadings is { Count: > 0 })
        {
            foreach (var (label, rawValue) in cachedReadings.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                var value = rawValue?.Trim();
                if (string.IsNullOrWhiteSpace(value) || !seenValues.Add(value))
                    continue;
                readings.Add(new OpenAvcReading { Label = label, Value = value });
            }
        }

        foreach (var (keys, label) in ReadingSources)
        {
            var value = TryReadStateValue(state, keys);
            if (value == null || !seenValues.Add(value))
                continue;
            readings.Add(new OpenAvcReading { Label = label, Value = value });
        }

        foreach (var kvp in state.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!IsDynamicReadingStateKey(kvp.Key))
                continue;

            var value = NormalizeReadingText(kvp.Value);
            if (string.IsNullOrWhiteSpace(value) || !seenValues.Add(value))
                continue;

            readings.Add(new OpenAvcReading
            {
                Label = HumanizeKey(kvp.Key),
                Value = value,
            });
        }

        var lastMessage = TryReadStateValue(state, "last_message", "lastMessage", "message");
        if (!string.IsNullOrWhiteSpace(lastMessage))
        {
            var messageValue = NormalizeReadingText(lastMessage);
            if (!string.IsNullOrWhiteSpace(messageValue)
                && !seenValues.Contains(messageValue)
                && !IsIgnorableReading(messageValue))
            {
                readings.Add(new OpenAvcReading { Label = "Last message", Value = messageValue });
                seenValues.Add(messageValue);
            }
        }

        if (string.IsNullOrWhiteSpace(lastRaw))
            return readings;

        var trimmed = lastRaw.Trim();
        var parsed = TryParseOkValue(trimmed);
        var responseValue = NormalizeReadingText(parsed ?? trimmed);
        if (!string.IsNullOrWhiteSpace(responseValue)
            && !seenValues.Contains(responseValue)
            && !IsIgnorableReading(responseValue))
            readings.Add(new OpenAvcReading { Label = "Last response", Value = responseValue });

        return readings;
    }

    private static string? TryParseOkValue(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var okQuoted = Regex.Match(raw, @"\+OK\s+""([^""]+)""", RegexOptions.IgnoreCase);
        if (okQuoted.Success)
            return okQuoted.Groups[1].Value.Trim();

        var okBare = Regex.Match(raw, @"\+OK\s+(\S+)", RegexOptions.IgnoreCase);
        if (okBare.Success)
            return okBare.Groups[1].Value.Trim().Trim('"');

        return null;
    }

    private static string? TryReadStateValue(Dictionary<string, string> state, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (state.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }
        return null;
    }

    private static (object? Result, string? Summary, string? Status) ParseOpenAvcResponseBody(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            string? status = null;
            if (root.TryGetProperty("status", out var statusEl) && statusEl.ValueKind == JsonValueKind.String)
                status = statusEl.GetString();

            object? result = null;
            string? summary = null;
            if (root.TryGetProperty("result", out var resultEl)
                && resultEl.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
            {
                result = JsonSerializer.Deserialize<object>(resultEl.GetRawText());
                summary = FormatResultSummary(resultEl);
            }

            var message = ReadString(root, "message") ?? ReadString(root, "output");
            if (string.IsNullOrWhiteSpace(summary) && !string.IsNullOrWhiteSpace(message))
                summary = message.Trim();

            if (result == null && root.TryGetProperty("output", out var outputEl)
                && outputEl.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
            {
                result = JsonSerializer.Deserialize<object>(outputEl.GetRawText());
                summary ??= FormatResultSummary(outputEl);
            }

            return (result, summary, status);
        }
        catch
        {
            return (null, null, null);
        }
    }

    private sealed class OpenAvcDeviceDetail
    {
        public string Id { get; set; } = "";
        public string? Name { get; set; }
        public string? Ip { get; set; }
        public string? DriverId { get; set; }
        public string? DriverName { get; set; }
        public List<OpenAvcCommandInfo> Commands { get; set; } = [];
        public Dictionary<string, string> State { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
