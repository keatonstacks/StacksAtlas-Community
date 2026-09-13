using System.Text.Json;
using Microsoft.Extensions.Logging;
using StacksAtlas.Core.Helpers;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Services.Security;

namespace StacksAtlas.Core.Settings;

public class FederationSettingsStore
{
    private readonly string _filePath;
    private readonly ILogger<FederationSettingsStore> _logger;
    private readonly Lock _lock = new();
    private FederationSettings _current;
    private SettingsEncryptor? _encryptor;

    public FederationSettingsStore(ILogger<FederationSettingsStore> logger)
    {
        _logger = logger;
        _filePath = Path.Combine(PlatformPaths.BaseDataDir, "federation_settings.json");
        _current = Load();
    }

    public void Initialize(SettingsEncryptor encryptor)
    {
        _encryptor = encryptor;
        _current = GetDecryptedVersion(_current);
    }

    public FederationSettings Current => _current;

    public FederationSettings Load()
    {
        lock (_lock)
        {
            if (!File.Exists(_filePath)) return new FederationSettings();

            try
            {
                var json = File.ReadAllText(_filePath);
                var settings = JsonSerializer.Deserialize<FederationSettings>(json) ?? new FederationSettings();
                
                // If we have an encryptor, decrypt now. Otherwise, we keep them as-is (possibly encrypted)
                return _encryptor != null ? GetDecryptedVersion(settings) : settings;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load federation settings. Using defaults.");
                return new FederationSettings();
            }
        }
    }

    public void Save(FederationSettings settings)
    {
        lock (_lock)
        {
            _current = settings;
            
            // Create a copy to encrypt for storage
            var toSave = new FederationSettings
            {
                Mode = settings.Mode,
                HubUrl = _encryptor?.Protect(settings.HubUrl) ?? settings.HubUrl,
                FederationToken = _encryptor?.Protect(settings.FederationToken) ?? settings.FederationToken,
                NodeId = settings.NodeId,
                AllowUntrustedHubs = settings.AllowUntrustedHubs,
                Client = settings.Client,
                Building = settings.Building,
                Room = settings.Room,
                SyncUsers = settings.SyncUsers,
                SyncUserRegistry = settings.SyncUserRegistry,
                SyncSsoSettings = settings.SyncSsoSettings,
                SyncAlertSettings = settings.SyncAlertSettings,
                SyncSiemSettings = settings.SyncSiemSettings,
                OverrideAlertSettings = settings.OverrideAlertSettings,
                OverrideSiemSettings = settings.OverrideSiemSettings,
                NodeDisplayName = settings.NodeDisplayName,
                UseTailscaleForHubConnection = settings.UseTailscaleForHubConnection,
                HubTailscaleMagicDns = settings.HubTailscaleMagicDns,
                HubTailscaleIpv4 = settings.HubTailscaleIpv4
            };

            var json = JsonSerializer.Serialize(toSave, new JsonSerializerOptions { WriteIndented = true });
            var tempPath = _filePath + ".tmp";
            
            try 
            {
                // Ensure any legacy read-only flags are stripped (common in locked down environments)
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
                
                OnSettingsChanged?.Invoke(settings);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError(ex, "ACCESS DENIED: Cannot write federation settings to {Path}", _filePath);
                throw new UnauthorizedAccessException(
                    $"ACCESS DENIED: StacksAtlas cannot write to '{_filePath}'. " +
                    "Ensure the application is running with sufficient privileges or the directory permissions allow write access.", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save federation settings.");
                throw;
            }
        }
    }

    private FederationSettings GetDecryptedVersion(FederationSettings s)
    {
        if (_encryptor == null) return s;

        return new FederationSettings
        {
            Mode = s.Mode,
            HubUrl = _encryptor.Unprotect(s.HubUrl),
            FederationToken = _encryptor.Unprotect(s.FederationToken),
            NodeId = s.NodeId,
            AllowUntrustedHubs = s.AllowUntrustedHubs,
            Client = s.Client,
            Building = s.Building,
            Room = s.Room,
            SyncUsers = s.SyncUsers,
            SyncUserRegistry = s.SyncUserRegistry,
            SyncSsoSettings = s.SyncSsoSettings,
            SyncAlertSettings = s.SyncAlertSettings,
            SyncSiemSettings = s.SyncSiemSettings,
            OverrideAlertSettings = s.OverrideAlertSettings,
            OverrideSiemSettings = s.OverrideSiemSettings,
            NodeDisplayName = s.NodeDisplayName,
            UseTailscaleForHubConnection = s.UseTailscaleForHubConnection,
            HubTailscaleMagicDns = s.HubTailscaleMagicDns,
            HubTailscaleIpv4 = s.HubTailscaleIpv4
        };
    }

    public event Action<FederationSettings>? OnSettingsChanged;
}
