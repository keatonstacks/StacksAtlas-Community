namespace StacksAtlas.Core.Services.Devices;

/// <summary>Maps OpenAVC device state keys into StacksAtlas asset fields.</summary>
public static class DeviceAssetMetadataExtractor
{
    private static readonly string[] SerialKeys = ["serial", "serial_number", "serialNumber"];
    private static readonly string[] FirmwareKeys = ["firmware", "firmware_version", "firmwareVersion", "fw_version"];

    public static string? ExtractSerial(IReadOnlyDictionary<string, string>? state) =>
        TryReadStateValue(state, SerialKeys);

    public static string? ExtractFirmware(IReadOnlyDictionary<string, string>? state) =>
        TryReadStateValue(state, FirmwareKeys);

    private static string? TryReadStateValue(IReadOnlyDictionary<string, string>? state, string[] keys)
    {
        if (state == null || state.Count == 0)
            return null;

        foreach (var key in keys)
        {
            if (state.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        foreach (var pair in state)
        {
            if (keys.Any(k => string.Equals(k, pair.Key, StringComparison.OrdinalIgnoreCase))
                && !string.IsNullOrWhiteSpace(pair.Value))
            {
                return pair.Value.Trim();
            }
        }

        return null;
    }
}
