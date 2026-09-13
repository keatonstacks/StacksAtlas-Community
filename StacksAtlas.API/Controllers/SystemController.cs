using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StacksAtlas.Core.Settings;
using StacksAtlas.Core.Services.Auth;
using StacksAtlas.Core.Services.Security;
using StacksAtlas.Core.State;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Helpers;
using StacksAtlas.Core.Data;
using StacksAtlas.API.Hubs;
using Serilog.Core;
using Serilog.Events;
using Microsoft.EntityFrameworkCore;
using StacksAtlas.API.Services;
using StacksAtlas.Core.Services.Updates;
using StacksAtlas.Core.Services.Audit;
using StacksAtlas.Core.Models.Updates;

namespace StacksAtlas.API.Controllers;

[Authorize(Roles = "Admin")]
[ApiController]
[Route("api/[controller]")]
public partial class SystemController(
    IHostApplicationLifetime lifetime, 
    SystemSettingsStore settingsStore,
    SettingsEncryptor encryptor,
    SsoDiagnosticService ssoDiagnostic,
    LoggingLevelSwitch levelSwitch,
    IFederationIdentitySyncService identitySync,
    ILogger<SystemController> logger,
    IAuditService audit,
    Microsoft.EntityFrameworkCore.IDbContextFactory<StacksAtlas.Core.Data.Hub.HubDbContext>? contextFactory = null,
    IHubContext<FederationHub>? hubContext = null,
    IFederatedNodeRepository? nodeRepo = null,
    IApplianceVersionProvider? versionProvider = null,
    IUpdateCheckService? updateCheckService = null,
    IUpdateApplyService? updateApplyService = null,
    IUpdateDepotService? updateDepotService = null) : ControllerBase
{
    private readonly IAuditService _audit = audit;
    private readonly IHostApplicationLifetime _lifetime = lifetime;
    private readonly SystemSettingsStore _settingsStore = settingsStore;
    private readonly SettingsEncryptor _encryptor = encryptor;
    private readonly SsoDiagnosticService _ssoDiagnostic = ssoDiagnostic;
    private readonly LoggingLevelSwitch _levelSwitch = levelSwitch;
    private readonly IFederationIdentitySyncService _identitySync = identitySync;
    private readonly Microsoft.EntityFrameworkCore.IDbContextFactory<StacksAtlas.Core.Data.Hub.HubDbContext>? _contextFactory = contextFactory;
    private readonly IHubContext<FederationHub>? _hubContext = hubContext;
    private readonly IFederatedNodeRepository? _nodeRepo = nodeRepo;
    private readonly ILogger<SystemController> _logger = logger;
    private readonly IApplianceVersionProvider? _versionProvider = versionProvider;
    private readonly IUpdateCheckService? _updateCheckService = updateCheckService;
    private readonly IUpdateApplyService? _updateApplyService = updateApplyService;
    private readonly IUpdateDepotService? _updateDepotService = updateDepotService;

    [System.Text.RegularExpressions.GeneratedRegex(@"^\[?(\d{4}-\d{2}-\d{2}[ T]\d{2}:\d{2}:\d{2}(?:\.\d+)?(?: ?[+-]\d{2}:?\d{2}| ?Z)?)\]?(.*)")]
    private static partial System.Text.RegularExpressions.Regex LogLineRegex();

    [HttpGet("config")]
    public IActionResult GetConfig()
    {
        var settings = _settingsStore.Load();
        return Ok(settings);
    }

    /// <summary>Authoritative appliance version and update channel  -  available without auth (no inventory sent).</summary>
    [HttpGet("version")]
    [AllowAnonymous]
    public IActionResult GetVersion()
    {
        if (_versionProvider == null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "Version provider unavailable." });

        var info = _versionProvider.GetCurrent();
        return Ok(info);
    }

    /// <summary>
    /// Opt-in update check  -  fetches signed manifest from configured CDN URLs only.
    /// Sends no device data; admin-only.
    /// </summary>
    [HttpPost("updates/check")]
    public async Task<IActionResult> CheckForUpdates([FromQuery] string? channel = null, CancellationToken cancellationToken = default)
    {
        if (_updateCheckService == null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "Update check unavailable." });

        var result = await _updateCheckService.CheckForUpdatesAsync(channel, cancellationToken);
        if (!result.UpdateAvailable)
        {
            var current = _versionProvider?.GetCurrent().Version ?? result.CurrentVersion;
            UpdateFleetPolicy.ResolvePendingHubUpdate(_settingsStore, current);
        }
        return Ok(result);
    }

    /// <summary>
    /// Download, verify, and apply an available update (Windows MSI or portable).
    /// Creates a pre-update snapshot before staging the platform installer.
    /// </summary>
    [HttpPost("updates/apply")]
    public async Task<IActionResult> ApplyUpdate([FromQuery] string? channel = null, CancellationToken cancellationToken = default)
    {
        if (_updateApplyService == null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "Update apply unavailable." });

        var result = await _updateApplyService.ApplyAsync(channel, cancellationToken);
        if (!string.Equals(result.Status, UpdateApplyStatuses.Failed, StringComparison.OrdinalIgnoreCase))
            UpdateFleetPolicy.ClearPendingHubUpdate(_settingsStore);
        return Ok(result);
    }

    /// <summary>Appliance-local scheduled apply preference (Slice 5). Portable cannot enable.</summary>
    [HttpGet("updates/schedule")]
    public IActionResult GetUpdateSchedule()
    {
        var schedule = UpdateFleetPolicy.NormalizeSchedule(_settingsStore.Load().UpdateSchedule);
        return Ok(schedule);
    }

    /// <summary>Persist appliance-local scheduled apply preference.</summary>
    [HttpPut("updates/schedule")]
    public IActionResult PutUpdateSchedule([FromBody] UpdateScheduleSettings? body)
    {
        var normalized = UpdateFleetPolicy.NormalizeSchedule(body);
        if (normalized.Enabled)
        {
            if (ExecutionState.IsPortable || (_versionProvider?.GetCurrent().IsPortable ?? false))
            {
                return BadRequest(new { message = "Scheduled apply is not available on portable appliances." });
            }

            if (ExecutionState.IsHubBrainEnabled)
            {
                return BadRequest(new { message = "Scheduled apply is not available on the Hub brain. Use Notify / Update now for enrolled sites." });
            }

            if (!OperatingSystem.IsWindows())
            {
                return BadRequest(new { message = "Scheduled apply is only available on Windows installed appliances." });
            }
        }

        var settings = _settingsStore.Load();
        settings.UpdateSchedule = normalized;
        _settingsStore.Save(settings);

        _audit.Record(
            AuditActions.UpdatesScheduleUpdate,
            "system_settings",
            "update_schedule",
            AuditOutcomes.Success,
            detail: $"enabled={normalized.Enabled}; day={normalized.DayOfWeek}; time={normalized.Hour:D2}:{normalized.Minute:D2}");

        return Ok(normalized);
    }

    /// <summary>Hub-pushed update offer waiting for local admin confirm (Slice 4).</summary>
    [HttpGet("updates/pending-hub")]
    public IActionResult GetPendingHubUpdate()
    {
        var current = _versionProvider?.GetCurrent().Version;
        var pending = UpdateFleetPolicy.ResolvePendingHubUpdate(_settingsStore, current);
        return Ok(pending);
    }

    /// <summary>Dismiss a Hub-pushed update offer without applying.</summary>
    [HttpDelete("updates/pending-hub")]
    public IActionResult ClearPendingHubUpdate()
    {
        UpdateFleetPolicy.ClearPendingHubUpdate(_settingsStore);
        return Ok(new { message = "Pending Hub update dismissed." });
    }

    /// <summary>
    /// Hub-only: list staged update packages under the local update depot (empty until stage ships).
    /// </summary>
    [HttpGet("updates/depot/status")]
    public IActionResult GetUpdateDepotStatus()
    {
        if (!ExecutionState.IsHub)
            return BadRequest("Update depot is only available in Hub mode.");

        if (_updateDepotService == null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "Update depot unavailable." });

        return Ok(_updateDepotService.GetStatus());
    }

    /// <summary>
    /// Hub-only: download and verify the channel release into the local update depot.
    /// </summary>
    [HttpPost("updates/stage")]
    public async Task<IActionResult> StageUpdateDepot([FromQuery] string? channel = null, CancellationToken cancellationToken = default)
    {
        if (!ExecutionState.IsHub)
            return BadRequest("Update depot is only available in Hub mode.");

        if (_updateDepotService == null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "Update depot unavailable." });

        var result = await _updateDepotService.StageAsync(channel, cancellationToken);
        if (!result.Success)
            return BadRequest(result);

        return Ok(result);
    }

    /// <summary>Runtime install mode  -  available during onboarding (no auth required).</summary>
    [HttpGet("info")]
    [AllowAnonymous]
    public IActionResult GetRuntimeInfo()
    {
        return Ok(new SystemRuntimeInfo(
            IsPortable: ExecutionState.IsPortable,
            InstallMode: PortableMode.InstallModeLabel,
            DataDirectory: PlatformPaths.BaseDataDir,
            PromoteHint: ExecutionState.IsPortable ? PortableMode.PromoteHint() : null));
    }

    [AllowAnonymous]
    [HttpGet("certificate")]
    public IActionResult GetCertificate()
    {
        var path = StacksAtlas.Core.Helpers.PlatformPaths.GetCertDirectory();
        var caPath = Path.Combine(path, ApplianceTlsTrust.CaFileName);
        var legacyLeafPath = Path.Combine(path, ApplianceTlsTrust.LeafFileName);
        var fullPath = System.IO.File.Exists(caPath) ? caPath : legacyLeafPath;

        if (!System.IO.File.Exists(fullPath)) return NotFound();

        var cert = global::System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12FromFile(
            fullPath,
            ApplianceTlsTrust.PfxPassword,
            global::System.Security.Cryptography.X509Certificates.X509KeyStorageFlags.EphemeralKeySet);
        var bytes = cert.Export(global::System.Security.Cryptography.X509Certificates.X509ContentType.Cert);

        return File(bytes, "application/x-x509-ca-cert", "StacksAtlas-LocalCA.cer");
    }

    /// <summary>TLS trust status for onboarding  -  no auth required.</summary>
    [AllowAnonymous]
    [HttpGet("certificate/status")]
    public IActionResult GetCertificateTrustStatus()
    {
        var certDir = PlatformPaths.GetCertDirectory();
        var caPath = Path.Combine(certDir, ApplianceTlsTrust.CaFileName);
        var leafPath = Path.Combine(certDir, ApplianceTlsTrust.LeafFileName);
        var ready = System.IO.File.Exists(leafPath);
        string? subject = null;

        if (System.IO.File.Exists(caPath))
        {
            try
            {
                var ca = global::System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12FromFile(
                    caPath,
                    ApplianceTlsTrust.PfxPassword,
                    global::System.Security.Cryptography.X509Certificates.X509KeyStorageFlags.EphemeralKeySet);
                subject = ca.Subject;
            }
            catch
            {
                ready = false;
            }
        }

        var caPresent = System.IO.File.Exists(caPath);
        var windows = OperatingSystem.IsWindows();
        var trustedInSession = windows && caPresent && ApplianceTlsTrust.IsTrustedInCurrentUserStore(caPath);
        var canOneClick = ApplianceTlsTrust.CanOneClickTrustOnWindows(ready, caPresent);

        return Ok(new
        {
            certificateReady = ready,
            isTrustedInSession = trustedInSession,
            canOneClickTrust = canOneClick,
            isPortable = ExecutionState.IsPortable,
            serverOs = windows ? "windows"
                : OperatingSystem.IsMacOS() ? "macos"
                : OperatingSystem.IsLinux() ? "linux"
                : "unknown",
            subject,
            hint = canOneClick
                ? "Click Trust on this PC  -  Windows will ask once. You can also keep using HTTP with no setup."
                : "Download the certificate and install it in your browser or OS trust store. Firefox requires a separate step."
        });
    }

    /// <summary>One-click TLS trust (portable helper or MSI scheduled task). No auth required.</summary>
    [AllowAnonymous]
    [HttpPost("certificate/trust")]
    public async Task<IActionResult> TrustApplianceCertificate(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Ok(new
            {
                trusted = false,
                method = "unavailable",
                message = "One-click trust is only available on Windows. Download the certificate instead."
            });
        }

        var caPath = Path.Combine(PlatformPaths.GetCertDirectory(), ApplianceTlsTrust.CaFileName);
        if (!System.IO.File.Exists(caPath))
        {
            return Ok(new
            {
                trusted = false,
                method = "pending",
                message = "Certificate is still being generated. Try again in a few seconds."
            });
        }

        if (ApplianceTlsTrust.IsTrustedInCurrentUserStore(caPath))
        {
            return Ok(new
            {
                trusted = true,
                method = "already-trusted",
                message = "This PC already trusts the StacksAtlas Local CA."
            });
        }

        if (!ApplianceTlsTrust.CanOneClickTrustOnWindows(certificateReady: true, caPresent: true))
        {
            return Ok(new
            {
                trusted = false,
                method = "unavailable",
                message = "One-click trust is not configured. Download the certificate instead."
            });
        }

        if (!ApplianceTlsTrust.TrySpawnWindowsTrustHelper(_logger))
        {
            return Ok(new
            {
                trusted = false,
                method = "spawn-failed",
                message = "Could not start the trust helper. Download the certificate and install it manually."
            });
        }

        // Helper runs in the interactive user session  -  API only polls.
        var trusted = await ApplianceTlsTrust.WaitForTrustedInCurrentUserStoreAsync(
            caPath,
            TimeSpan.FromSeconds(60),
            msg => _logger.LogDebug("Onboarding TLS trust: {Message}", msg),
            cancellationToken);

        return Ok(new
        {
            trusted,
            method = ExecutionState.IsPortable ? "portable-spawn" : "scheduled-task",
            message = trusted
                ? "StacksAtlas Local CA trusted on this PC. Close and reopen Chrome before using HTTPS."
                : "Trust did not complete in time. Click Yes on the Windows prompt, or continue with HTTP."
        });
    }

    /// <summary>Preferred dashboard URL (HTTP default; HTTPS when opted in and CA trusted).</summary>
    [AllowAnonymous]
    [HttpGet("dashboard-url")]
    public IActionResult GetDashboardUrl()
    {
        var settings = _settingsStore.Current;
        var url = ApplianceDashboardUrls.Resolve(settings, PlatformPaths.BaseDataDir);
        var caPath = Path.Combine(PlatformPaths.GetCertDirectory(), ApplianceTlsTrust.CaFileName);

        return Ok(new
        {
            url,
            scheme = ApplianceDashboardUrls.WantsHttps(settings) ? "https" : "http",
            httpsAvailable = ApplianceDashboardUrls.CanUseHttps(caPath)
        });
    }

    /// <summary>Remember operator preference for tray/shortcut behavior (HTTP vs HTTPS). Admin only.</summary>
    [HttpPut("dashboard-preference")]
    public IActionResult SetDashboardPreference([FromBody] DashboardPreferenceRequest request)
    {
        var scheme = request.Scheme?.Trim().ToLowerInvariant();
        if (scheme is not ("http" or "https"))
            return BadRequest(new { message = "Scheme must be 'http' or 'https'." });

        var settings = _settingsStore.Load();
        settings.DashboardScheme = scheme;
        _settingsStore.Save(settings);

        var url = ApplianceDashboardUrls.Resolve(settings, PlatformPaths.BaseDataDir);
        return Ok(new { scheme, url });
    }

    public sealed record DashboardPreferenceRequest(string? Scheme);

    [HttpPost("ports")]
    public IActionResult UpdatePorts([FromBody] SystemSettings settings)
    {
        if (settings.HttpPort <= 0 || settings.HttpsPort <= 0)
        {
            return BadRequest(new { message = "Invalid port numbers." });
        }

        _settingsStore.Save(settings);
        _audit.Record(
            AuditActions.SettingsPortsUpdate,
            "settings",
            "ports",
            AuditOutcomes.Success,
            detail: $"http={settings.HttpPort}; https={settings.HttpsPort}; syslog={settings.SyslogEnabled}");
        return Ok(new { message = "Ports updated. A restart is required for changes to take effect." });
    }

    [HttpPost("logging")]
    public async Task<IActionResult> ToggleLogging([FromQuery] bool enabled, [FromQuery] string? nodeId = null)
    {
        if (!string.IsNullOrWhiteSpace(nodeId))
        {
            if (!ExecutionState.IsHub || _hubContext == null || _nodeRepo == null)
                return BadRequest("Remote logging control requires Hub mode.");

            var node = _nodeRepo.GetById(nodeId);
            if (node == null) return NotFound("Node not found.");
            if (string.IsNullOrEmpty(node.ConnectionId))
                return BadRequest("Node is offline. Connect the node before toggling debug logging.");

            var command = new FederationCommand
            {
                CommandType = "ConfigureDebugLogging",
                Parameters = new Dictionary<string, string>
                {
                    { "enabled", enabled ? "true" : "false" }
                }
            };

            try
            {
                var result = await _hubContext.Clients.Client(node.ConnectionId)
                    .InvokeAsync<FederationCommandResult>("ExecuteCommand", command, default);
                if (result is { Success: false })
                    return BadRequest(result.Message ?? "Node rejected debug logging change.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to toggle debug logging on Node {NodeId}", nodeId);
                return StatusCode(StatusCodes.Status503ServiceUnavailable,
                    new { message = "Node is unreachable. Try again when the site is online." });
            }

            node.IsDebugLoggingEnabled = enabled;
            _nodeRepo.UpsertNode(node);

            _audit.Record(
                AuditActions.SettingsLoggingToggle,
                "settings",
                "logging",
                AuditOutcomes.Success,
                detail: $"enabled={enabled}; nodeId={nodeId}");

            return Ok(new
            {
                enabled,
                nodeId,
                message = $"Debug logging {(enabled ? "enabled" : "disabled")} on Node {nodeId}."
            });
        }

        var settings = _settingsStore.Load();
        settings.IsDebugLoggingEnabled = enabled;
        _settingsStore.Save(settings);

        _levelSwitch.MinimumLevel = enabled 
            ? LogEventLevel.Debug 
            : LogEventLevel.Information;

        _audit.Record(
            AuditActions.SettingsLoggingToggle,
            "settings",
            "logging",
            AuditOutcomes.Success,
            detail: $"enabled={enabled}");

        return Ok(new { enabled, message = $"Logging level set to {(enabled ? "Debug" : "Information")}." });
    }

    [HttpPost("restart")]
    public IActionResult Restart()
    {
        // Trigger shutdown in a background task to allow the 200 OK to return to the client first.
        // We use Environment.Exit(0) instead of StopApplication() because on Windows, StopApplication()
        // only gracefully shuts down Kestrel but does NOT trigger a restart. Environment.Exit(0) signals
        // a clean exit to the Windows Service Control Manager (SCM), which will restart the service
        // automatically when the service recovery policy is set to "Restart the Service".
        // On Linux/Docker, systemd or the container runtime handles the restart via RestartPolicy.
        _ = Task.Run(async () =>
        {
            await Task.Delay(1500);
            Environment.Exit(0);
        });

        return Ok(new { message = "Restart command issued. The service will shut down and restart automatically." });
    }

    [HttpGet("logs")]
    public IActionResult GetLogs([FromQuery] int limit = 200)
    {
        var baseDataDir = StacksAtlas.Core.Helpers.PlatformPaths.BaseDataDir;
        var logDirectory = Path.Combine(baseDataDir, "logs");
        
        if (!Directory.Exists(logDirectory)) return Ok(new List<string>());

        // Sort by LastWriteTime to ensure the active log file currently being written to is always read first
        var logFiles = Directory.GetFiles(logDirectory, "log-*.txt")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTime)
            .Select(f => f.FullName)
            .ToList();

        if (!logFiles.Any()) return Ok(new List<string>());

        var latestFile = logFiles.First();
        
        try
        {
            // Use FileStream with FileShare.ReadWrite to prevent locking issues with Serilog
            using var fs = new FileStream(latestFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs);
            
            var lines = new List<string>();
            while (!reader.EndOfStream)
            {
                lines.Add(reader.ReadLine() ?? "");
            }

            var rawLines = lines.TakeLast(Math.Clamp(limit, 1, 2000)).ToList();
            var parsedLines = new List<string>();
            foreach (var line in rawLines)
            {
                var match = LogLineRegex().Match(line);
                if (match.Success)
                {
                    var timestampStr = match.Groups[1].Value.Trim();
                    var restOfLine = match.Groups[2].Value;
                    if (DateTimeOffset.TryParse(timestampStr, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal, out var dto))
                    {
                        parsedLines.Add($"[{dto.UtcDateTime:yyyy-MM-dd HH:mm:ss.fff} +00:00]{restOfLine}");
                        continue;
                    }
                }
                parsedLines.Add(line);
            }
            return Ok(parsedLines);
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Failed to read log file: {ex.Message}");
        }
    }

    [HttpGet("logs/remote")]
    public async Task<IActionResult> GetRemoteLogs([FromQuery] string nodeId, [FromQuery] int limit = 200)
    {
        if (_contextFactory == null)
        {
            return BadRequest("Remote logs are only available in Hub mode.");
        }

        try
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            var logs = await context.FederatedLogs
                .Where(l => l.NodeId == nodeId)
                .OrderByDescending(l => l.Timestamp)
                .Take(Math.Clamp(limit, 1, 2000))
                .ToListAsync();

            // Format logs to mimic local Serilog file lines for UI terminal compatibility
            var formattedLines = logs.Select(l =>
            {
                var utcTimestamp = DateTime.SpecifyKind(l.Timestamp, DateTimeKind.Utc);
                var lvl = FormatSerilogLevelAbbreviation(l.LogLevel);
                var exc = string.IsNullOrEmpty(l.Exception) ? "" : $"\n{l.Exception}";
                return $"[{utcTimestamp:yyyy-MM-dd HH:mm:ss.fff} +00:00] [{lvl}] {l.Message}{exc}";
            })
            .Reverse()
            .ToList();

            return Ok(formattedLines);
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Failed to retrieve remote logs: {ex.Message}");
        }
    }

    /// <summary>Maps Serilog level names (or short tokens) to Serilog file abbreviations.</summary>
    private static string FormatSerilogLevelAbbreviation(string? logLevel)
    {
        if (string.IsNullOrWhiteSpace(logLevel))
            return "INF";

        return logLevel.Trim().ToUpperInvariant() switch
        {
            "VERBOSE" or "VRB" => "VRB",
            "DEBUG" or "DBG" or "DEB" => "DBG",
            "INFORMATION" or "INFO" or "INF" => "INF",
            "WARNING" or "WARN" or "WRN" or "WAR" => "WRN",
            "ERROR" or "ERR" => "ERR",
            "FATAL" or "FTL" or "FAT" => "FTL",
            _ => logLevel.Length >= 3
                ? logLevel.Substring(0, 3).ToUpperInvariant()
                : logLevel.ToUpperInvariant()
        };
    }

    [HttpGet("auth")]
    public IActionResult GetAuthSettings()
    {
        var settings = _settingsStore.Load();
        var auth = settings.Auth;
        
        // Mask sensitive fields for the UI
        var response = new AuthSettings
        {
            SsoEnabled = auth.SsoEnabled,
            Provider = auth.Provider,
            DefaultRole = auth.DefaultRole,
            RoleMappings = auth.RoleMappings,
            Ldap = auth.Ldap,
            Oidc = new OidcSettings
            {
                Authority = auth.Oidc.Authority,
                ClientId = auth.Oidc.ClientId,
                Scope = auth.Oidc.Scope,
                ClientSecret = "********" // Masked
            }
        };

        return Ok(response);
    }

    [HttpPut("auth")]
    public async Task<IActionResult> UpdateAuthSettings([FromBody] AuthSettings newAuth)
    {
        var currentSettings = _settingsStore.Load();
        
        // USER REQUIREMENT: Write-Only Pattern
        if (newAuth.Oidc.ClientSecret == "********")
        {
            newAuth.Oidc.ClientSecret = currentSettings.Auth.Oidc.ClientSecret;
        }
        else if (!string.IsNullOrEmpty(newAuth.Oidc.ClientSecret))
        {
            newAuth.Oidc.ClientSecret = _encryptor.Protect(newAuth.Oidc.ClientSecret);
        }

        currentSettings.Auth = newAuth;
        _settingsStore.Save(currentSettings);
        
        await _identitySync.BroadcastIdentityStateAsync();

        _audit.Record(
            AuditActions.SettingsAuthUpdate,
            "settings",
            "auth",
            AuditOutcomes.Success,
            detail: $"ssoEnabled={newAuth.SsoEnabled}; provider={newAuth.Provider}");
        
        return Ok(new { message = "Authentication settings updated successfully." });
    }

    [HttpPost("auth/test-oidc")]
    public async Task<IActionResult> TestOidc([FromBody] OidcSettings settings)
    {
        // If they provided the mask, use the existing secret for the test
        if (settings.ClientSecret == "********")
        {
            var current = _settingsStore.Load();
            settings.ClientSecret = _encryptor.Unprotect(current.Auth.Oidc.ClientSecret);
        }

        var result = await _ssoDiagnostic.TestOidcAsync(settings);
        if (!result.Success) return BadRequest(new { message = result.Message });
        return Ok(new { message = result.Message });
    }

    [HttpPost("auth/test-ldap")]
    public IActionResult TestLdap([FromBody] LdapSettings settings)
    {
        var result = _ssoDiagnostic.TestLdap(settings);
        if (!result.Success) return BadRequest(new { message = result.Message });
        return Ok(new { message = result.Message });
    }
}
