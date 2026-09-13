using Microsoft.Extensions.Logging;
using StacksAtlas.Core.Helpers;

namespace StacksAtlas.Core.Services.Governance;

/// <summary>
/// Resets operator JSON settings while preserving license.key and db.key.
/// </summary>
public static class FactorySettingsReset
{
    private const string FactoryResetFlagFileName = ".factoryreset";

    public static string GetFlagPath() =>
        Path.Combine(PlatformPaths.BaseDataDir, FactoryResetFlagFileName);

    public static void Stage() =>
        File.WriteAllText(GetFlagPath(), "TRUE");

    public static bool TryApply(ILogger? logger = null)
    {
        var flagPath = GetFlagPath();
        if (!File.Exists(flagPath))
            return false;

        try
        {
            DeleteIfExists(PlatformPaths.GetSystemSettingsPath());
            DeleteIfExists(PlatformPaths.GetNetworkSettingsPath());
            DeleteIfExists(Path.Combine(PlatformPaths.BaseDataDir, "federation_settings.json"));
            DeleteIfExists(Path.Combine(PlatformPaths.BaseDataDir, "pollingsettings.json"));
            DeleteIfExists(Path.Combine(PlatformPaths.BaseDataDir, "cleanupsettings.json"));

            File.Delete(flagPath);
            logger?.LogInformation(
                "Factory reset applied. JSON settings removed; license.key and db.key preserved.");
            return true;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to apply factory settings reset.");
            return false;
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}
