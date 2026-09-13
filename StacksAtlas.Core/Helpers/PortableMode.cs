using System.Runtime.InteropServices;

namespace StacksAtlas.Core.Helpers;

/// <summary>
/// Portable / evaluation mode  -  user-session process, no system service registration.
/// Activated via STACKSATLAS_PORTABLE=1 before the API starts.
/// </summary>
public static class PortableMode
{
    public static bool IsEnabled { get; private set; }

    /// <summary>
    /// When portable + STACKSATLAS_PORTABLE_BIND_LAN=1, listen on all interfaces (Docker headless / NAS).
    /// Win/Mac portable EXE defaults to loopback-only.
    /// </summary>
    public static bool AllowLanBind { get; private set; }

    public static void InitializeFromEnvironment()
    {
        var value = Environment.GetEnvironmentVariable("STACKSATLAS_PORTABLE");
        IsEnabled = IsTruthy(value);
        AllowLanBind = IsEnabled && IsTruthy(Environment.GetEnvironmentVariable("STACKSATLAS_PORTABLE_BIND_LAN"));
    }

    private static bool IsTruthy(string? value) =>
        string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    public static string DefaultTrialDataDirectory()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "StacksAtlas",
                "data");
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "StacksAtlas-Trial");
    }

    public static string DefaultBundleExtractDirectory(string dataDirectory) =>
        Path.Combine(dataDirectory, ".dotnet-bundle");

    /// <summary>Absolute path to StacksAtlas-Portable.exe  -  passed to the API for detached wipe helpers.</summary>
    public const string LauncherPathVariable = "STACKSATLAS_PORTABLE_LAUNCHER";

    public static string InstallModeLabel => IsEnabled ? "portable" : "appliance";

    public static string PromoteHint()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return "Run Install StacksAtlas.app from the DMG to register the background service and move to production mode.";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "Install StacksAtlas.msi (background service) for always-on appliance mode.";
        }

        return "Use the production install path for always-on appliance deployment.";
    }
}
