using System.Text.Json;
using Microsoft.Extensions.Logging;
using StacksAtlas.Core.Helpers;

namespace StacksAtlas.Core.Settings;

public class SystemSettings
{
    public int Version { get; set; } = 1;
    public int HttpPort { get; set; } = 5000;
    public int HttpsPort { get; set; } = 5001;
    public bool IsDebugLoggingEnabled { get; set; } = false;

    // Syslog Settings
    public bool SyslogEnabled { get; set; } = false;
    public string SyslogHost { get; set; } = string.Empty;
    public int SyslogPort { get; set; } = 514;
    public string SyslogAppName { get; set; } = "StacksAtlas";

    // SSO Settings
    public AuthSettings Auth { get; set; } = new();

    // Database Settings
    public DatabaseSettings Database { get; set; } = new();

    // Onboarding / foundation wizard (roadmap §7.7)
    public OnboardingSettings Onboarding { get; set; } = new();

    /// <summary>Operator dashboard entry: "http" (default) or "https" after local CA trust.</summary>
    public string DashboardScheme { get; set; } = "http";

    /// <summary>Weekly maintenance window for appliance-local scheduled apply (1.9.2 Slice 5).</summary>
    public UpdateScheduleSettings UpdateSchedule { get; set; } = new();

    /// <summary>Hub-notified update waiting for local admin confirm (1.9.2 Slice 4).</summary>
    public PendingHubUpdateSettings PendingHubUpdate { get; set; } = new();
}

public class DatabaseSettings
{
    public string Provider { get; set; } = "Sqlite"; // Sqlite, PostgreSQL, SQLServer
    public string ConnectionString { get; set; } = string.Empty;
}

public class AuthSettings
{
    public bool SsoEnabled { get; set; } = false;
    public string Provider { get; set; } = "OIDC"; // OIDC or LDAP
    public string DefaultRole { get; set; } = "Viewer";
    public OidcSettings Oidc { get; set; } = new();
    public LdapSettings Ldap { get; set; } = new();
    public List<RoleMapping> RoleMappings { get; set; } = new();
}

public class OidcSettings
{
    public string Authority { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Scope { get; set; } = "openid profile email";
}

public class LdapSettings
{
    public string Server { get; set; } = string.Empty;
    public int Port { get; set; } = 389;
    public bool UseSsl { get; set; } = false;
    public string BaseDn { get; set; } = string.Empty;
    public string UserFilter { get; set; } = "(sAMAccountName={0})";
    public string GroupAttribute { get; set; } = "memberOf";
}

public class RoleMapping
{
    public string ExternalGroup { get; set; } = string.Empty;
    public string StacksAtlasRole { get; set; } = string.Empty; // Admin, Standard, Viewer
}

public class SystemSettingsStore
{
    private readonly string _filePath;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ILogger<SystemSettingsStore> _logger;
    private readonly Lock _lock = new();

    public event Action? OnSettingsChanged;

    public SystemSettings Current { get; private set; } = new();

    public SystemSettingsStore(ILogger<SystemSettingsStore> logger)
    {
        _logger = logger;
        _filePath = StacksAtlas.Core.Helpers.PlatformPaths.GetSystemSettingsPath();

        var dir = Path.GetDirectoryName(_filePath);
        if (dir != null) Directory.CreateDirectory(dir);

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        Load(); // Initial load to populate Current
    }

    public SystemSettings Load()
    {
        lock (_lock)
        {
            try
            {
                if (!File.Exists(_filePath))
                {
                    Current = new SystemSettings();
                    Save(Current);
                    return Current;
                }

                var json = File.ReadAllText(_filePath);
                var settings = JsonSerializer.Deserialize<SystemSettings>(json, _jsonOptions) ?? new SystemSettings();

                var httpBefore = settings.HttpPort;
                Validate(settings);
                Current = settings;
                if (httpBefore != settings.HttpPort)
                {
                    _logger.LogWarning(
                        "Adjusted HTTP port {OldPort} -> {NewPort} (macOS AirPlay uses port 5000). Persisting system settings.",
                        httpBefore, settings.HttpPort);
                    PersistValidated(settings);
                }
                return Current;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load system settings. Using defaults.");
                Current = new SystemSettings();
                return Current;
            }
        }
    }

    public void Save(SystemSettings settings)
    {
        lock (_lock)
        {
            try
            {
                Validate(settings);
                var json = JsonSerializer.Serialize(settings, _jsonOptions);
                var tempPath = _filePath + ".tmp";

                // Ensure any legacy read-only flags are stripped from the target file
                if (File.Exists(_filePath))
                {
                    var attr = File.GetAttributes(_filePath);
                    if (attr.HasFlag(FileAttributes.ReadOnly))
                    {
                        File.SetAttributes(_filePath, attr & ~FileAttributes.ReadOnly);
                    }
                }
                
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, _filePath, overwrite: true);
                
                Current = settings;
                _logger.LogInformation("System settings saved successfully.");
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError(ex, "ACCESS DENIED: Cannot write system settings to {Path}", _filePath);
                throw new UnauthorizedAccessException(
                    $"ACCESS DENIED: StacksAtlas cannot write to '{_filePath}'. " +
                    "Ensure the application has write permissions to the data directory.", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save system settings.");
                throw;
            }
        }
        OnSettingsChanged?.Invoke();
    }

    private void Validate(SystemSettings settings)
    {
        if (settings.HttpPort <= 0 || settings.HttpPort > 65535)
            settings.HttpPort = AppliancePortDefaults.DefaultHttpPortForPlatform();
        else
            settings.HttpPort = AppliancePortDefaults.ResolveHttpPort(settings.HttpPort);

        if (settings.HttpsPort <= 0 || settings.HttpsPort > 65535)
            settings.HttpsPort = AppliancePortDefaults.StandardHttpsPort;
        if (settings.HttpPort == settings.HttpsPort) settings.HttpsPort = settings.HttpPort + 1;

        // Syslog Validation
        if (settings.SyslogPort <= 0 || settings.SyslogPort > 65535) settings.SyslogPort = 514;
        if (string.IsNullOrWhiteSpace(settings.SyslogAppName)) settings.SyslogAppName = "StacksAtlas";
    }

    private void PersistValidated(SystemSettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, _jsonOptions);
            var tempPath = _filePath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _filePath, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not persist migrated HTTP port to {Path}", _filePath);
        }
    }
}
