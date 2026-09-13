using System.Text.Json;

namespace StacksAtlas.Core.Settings;

// IDE0290: Primary constructor for cleaner dependency injection
public class PollingSettingsStore(string filePath)
{
    private readonly string _filePath = filePath;

    // IDE0330: Use the new dedicated Lock type for better performance
    private static readonly Lock _fileLock = new();

    public event Action? OnSettingsChanged;

    // CA1869: Cached instance to prevent performance degradation during serialization
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    // Cached in-memory copy for high-frequency access
    public PollingOptions Current { get; private set; } = new();

    public PollingOptions Load()
    {
        lock (_fileLock)
        {
            try
            {
                if (!File.Exists(_filePath))
                {
                    Current = new PollingOptions();
                    Save(Current);
                    return Current;
                }

                var json = File.ReadAllText(_filePath);
                var options = JsonSerializer.Deserialize<PollingOptions>(json, _jsonOptions) ?? new PollingOptions();
                
                Validate(options);
                Current = options;
                return Current;
            }
            catch
            {
                Current = new PollingOptions();
                return Current;
            }
        }
    }

    public void Save(PollingOptions options)
    {
        lock (_fileLock)
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
            }
            catch
            {
                throw;
            }
        }
        OnSettingsChanged?.Invoke();
    }

    private void Validate(PollingOptions options)
    {
        if (options.IntervalSeconds < 1) options.IntervalSeconds = 10;
        if (options.RefreshIntervalSeconds < 1) options.RefreshIntervalSeconds = 30;
        if (options.OfflineAfterSeconds < 60) options.OfflineAfterSeconds = 300;
    }
}
