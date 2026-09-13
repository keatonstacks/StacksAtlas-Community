using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace StacksAtlas.Core.Settings;

public class CleanupSettingsStore
{
    private readonly string _filePath;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ILogger<CleanupSettingsStore> _logger;
    private readonly Lock _lock = new();

    public event Action? OnSettingsChanged;

    public CleanupOptions Current { get; private set; } = new();

    public CleanupSettingsStore(ILogger<CleanupSettingsStore> logger, Microsoft.Extensions.Options.IOptions<CleanupOptions> initialOptions)
    {
        _logger = logger;
        _filePath = Path.Combine(StacksAtlas.Core.Helpers.PlatformPaths.BaseDataDir, "cleanupsettings.json");

        var dir = Path.GetDirectoryName(_filePath);
        if (dir != null) Directory.CreateDirectory(dir);

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        if (!File.Exists(_filePath))
        {
            _logger.LogInformation("Migrating cleanup settings from appsettings.json to indestructible store...");
            Save(initialOptions.Value);
        }

        Load();
    }

    public CleanupOptions Load()
    {
        lock (_lock)
        {
            try
            {
                if (!File.Exists(_filePath))
                {
                    // For the very first run, we'll stay with defaults
                    // (Migration from appsettings.json happens in Program.cs if needed)
                    Current = new CleanupOptions();
                    Save(Current);
                    return Current;
                }

                var json = File.ReadAllText(_filePath);
                var options = JsonSerializer.Deserialize<CleanupOptions>(json, _jsonOptions) ?? new CleanupOptions();
                
                Validate(options);
                Current = options;
                return Current;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load cleanup settings. Using defaults.");
                Current = new CleanupOptions();
                return Current;
            }
        }
    }

    public void Save(CleanupOptions options)
    {
        lock (_lock)
        {
            try
            {
                Validate(options);
                var json = JsonSerializer.Serialize(options, _jsonOptions);
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
                
                Current = options;
                _logger.LogInformation("Cleanup settings saved successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save cleanup settings.");
                throw;
            }
        }
        OnSettingsChanged?.Invoke();
    }

    private void Validate(CleanupOptions options)
    {
        if (options.RunEveryMinutes < 1) options.RunEveryMinutes = 5;
        if (options.AutoArchiveDays < 1) options.AutoArchiveDays = 30;
        if (options.PermanentDeletionDays < -1) options.PermanentDeletionDays = 90;
        if (options.SweepHistoryRetentionHours < 1) options.SweepHistoryRetentionHours = 24;
    }
}
