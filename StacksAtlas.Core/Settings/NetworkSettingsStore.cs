using System.Text.Json;
using Microsoft.Extensions.Logging;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Helpers;

namespace StacksAtlas.Core.Settings; // File-scoped namespace

public class NetworkSettings
{
    public int Version { get; set; } = 1;
    // IDE0028: Simplified collection initialization
    public List<NetworkScope> Subnets { get; set; } = [];
    /// <summary>Per-adapter roles and default VLAN tracking label (7.1.2+).</summary>
    public List<NetworkInterfaceConfig> InterfaceConfigs { get; set; } = [];
    /// <summary>Flattened role map maintained from InterfaceConfigs for outbound binding.</summary>
    public List<InterfaceRoleMapping> InterfaceRoleMappings { get; set; } = [];
    public int PingTimeoutMs { get; set; } = 200;
    public int PingRetries { get; set; } = 0;
    public int MaxParallelPings { get; set; } = 256;
    
    // Hysteresis / Stability Settings
    public int OfflineStrikeThreshold { get; set; } = 3;      // Pings to miss before offline
    public bool EnableInstantRecovery { get; set; } = true;   // Immediately online on success

    // Nmap Deep Scanning
    public bool EnableNmapDeepScan { get; set; } = false;
    public int MaxConcurrentDeepScans { get; set; } = 2;
}

public class NetworkSettingsStore
{
    private readonly string _filePath;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ILogger<NetworkSettingsStore> _logger;
    private readonly Lock _lock = new();

    public event Action? OnSettingsChanged;

    public NetworkSettings Current { get; private set; } = new();

    public NetworkSettingsStore(ILogger<NetworkSettingsStore> logger)
    {
        _logger = logger;
        _filePath = StacksAtlas.Core.Helpers.PlatformPaths.GetNetworkSettingsPath();

        var dir = Path.GetDirectoryName(_filePath);
        if (dir != null) Directory.CreateDirectory(dir);

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        Load(); // Initial load to populate Current
    }

    public NetworkSettings Load()
    {
        lock (_lock)
        {
            try
            {
                if (!File.Exists(_filePath))
                {
                    Current = new NetworkSettings();
                    Save(Current);
                    return Current;
                }

                var json = File.ReadAllText(_filePath);
                var settings = JsonSerializer.Deserialize<NetworkSettings>(json, _jsonOptions) ?? new NetworkSettings();
                
                Validate(settings);
                Current = settings;
                return Current;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load network settings. Using defaults.");
                Current = new NetworkSettings();
                return Current;
            }
        }
    }

    /// <summary>
    /// Applies Hub-governed network settings without wiping fields omitted from the patch payload.
    /// </summary>
    public void ApplyHubGovernancePatch(NetworkSettings patch)
    {
        var current = Load();

        if (patch.Subnets is { Count: > 0 })
            current.Subnets = patch.Subnets;

        if (patch.InterfaceConfigs != null)
            current.InterfaceConfigs = patch.InterfaceConfigs;

        if (patch.InterfaceRoleMappings is { Count: > 0 })
            current.InterfaceRoleMappings = patch.InterfaceRoleMappings;

        current.PingTimeoutMs = patch.PingTimeoutMs;
        current.PingRetries = patch.PingRetries;
        current.MaxParallelPings = patch.MaxParallelPings;
        current.OfflineStrikeThreshold = patch.OfflineStrikeThreshold;
        current.EnableInstantRecovery = patch.EnableInstantRecovery;
        current.EnableNmapDeepScan = patch.EnableNmapDeepScan;
        current.MaxConcurrentDeepScans = patch.MaxConcurrentDeepScans;

        Save(current);
    }

    public void Save(NetworkSettings settings)
    {
        lock (_lock)
        {
            try
            {
                Validate(settings);
                var json = JsonSerializer.Serialize(settings, _jsonOptions);
                var tempPath = _filePath + ".tmp";

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
                
                Current = settings;
                _logger.LogInformation("Network settings saved successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save network settings.");
                throw;
            }
        }
        OnSettingsChanged?.Invoke();
    }

    private void Validate(NetworkSettings settings)
    {
        // Prevent impossible timeouts or parallel counts
        if (settings.PingTimeoutMs < 10) settings.PingTimeoutMs = 200;
        if (settings.MaxParallelPings < 1) settings.MaxParallelPings = 256;
        if (settings.MaxParallelPings > 2048) settings.MaxParallelPings = 2048;
        if (settings.OfflineStrikeThreshold < 1) settings.OfflineStrikeThreshold = 1;
        
        settings.Subnets ??= [];
        settings.InterfaceConfigs ??= [];
        settings.InterfaceRoleMappings ??= [];

        MigrateLegacyInterfaceMappings(settings);

        foreach (var config in settings.InterfaceConfigs)
        {
            if (string.IsNullOrWhiteSpace(config.InterfaceId))
                continue;

            if (!VlanTagValidator.TryNormalize(config.DefaultVlanTag, out var vlanNormalized, out var vlanError))
                throw new InvalidOperationException($"Adapter {config.InterfaceId}: {vlanError}");

            config.DefaultVlanTag = vlanNormalized;
            config.Roles ??= [];
        }

        SyncRoleMappingsFromConfigs(settings);

        foreach (var scope in settings.Subnets)
        {
            if (string.IsNullOrWhiteSpace(scope.Id))
                scope.Id = Guid.NewGuid().ToString("N");

            if (!VlanTagValidator.TryNormalize(scope.VlanTag, out var normalized, out var error))
                throw new InvalidOperationException(error ?? "Invalid VLAN tag.");

            scope.VlanTag = normalized;
        }
    }

    private static void MigrateLegacyInterfaceMappings(NetworkSettings settings)
    {
        if (settings.InterfaceConfigs.Count > 0 || settings.InterfaceRoleMappings.Count == 0)
            return;

        foreach (var group in settings.InterfaceRoleMappings.GroupBy(m => m.InterfaceId))
        {
            if (string.IsNullOrWhiteSpace(group.Key))
                continue;

            settings.InterfaceConfigs.Add(new NetworkInterfaceConfig
            {
                InterfaceId = group.Key,
                Roles = group.Select(m => m.Role).Distinct().ToList()
            });
        }
    }

    private static void SyncRoleMappingsFromConfigs(NetworkSettings settings)
    {
        settings.InterfaceRoleMappings = settings.InterfaceConfigs
            .Where(c => !string.IsNullOrWhiteSpace(c.InterfaceId))
            .SelectMany(c => c.Roles.Distinct().Select(role => new InterfaceRoleMapping
            {
                InterfaceId = c.InterfaceId,
                Role = role
            }))
            .ToList();
    }

    /// <summary>Scope override, then adapter default, for discovery VLAN tracking.</summary>
    public static string? ResolveDiscoveryVlanTag(NetworkSettings settings, string? interfaceId, string? scopeVlanTag)
    {
        if (!string.IsNullOrWhiteSpace(scopeVlanTag))
            return scopeVlanTag.Trim();

        if (string.IsNullOrWhiteSpace(interfaceId))
            return null;

        return settings.InterfaceConfigs
            .FirstOrDefault(c => c.InterfaceId == interfaceId)
            ?.DefaultVlanTag;
    }
}
