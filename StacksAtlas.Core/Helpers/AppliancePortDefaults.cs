namespace StacksAtlas.Core.Helpers;

/// <summary>
/// Platform-aware default appliance ports. macOS Monterey+ reserves TCP 5000 for AirPlay Receiver.
/// </summary>
public static class AppliancePortDefaults
{
    public const int StandardHttpPort = 5000;
    public const int StandardHttpsPort = 5001;
    public const int MacHttpPort = 5050;

    public static int DefaultHttpPortForPlatform() =>
        OperatingSystem.IsMacOS() ? MacHttpPort : StandardHttpPort;

    public static int ResolveHttpPort(int configuredPort) =>
        OperatingSystem.IsMacOS() && configuredPort == StandardHttpPort
            ? MacHttpPort
            : configuredPort;

    /// <summary>
    /// Normalize HTTP port for a remote federated node (Hub quick links, pulse probes).
    /// macOS nodes may still report 5000 from legacy settings while listening on 5050.
    /// </summary>
    public static int ResolveHttpPortForNode(int httpPort, string? osDescription)
    {
        var port = httpPort > 0 ? httpPort : StandardHttpPort;
        if (IsMacOsDescription(osDescription) && port == StandardHttpPort)
            return MacHttpPort;
        return port;
    }

    public static int ResolveHttpsPortForNode(int httpsPort) =>
        httpsPort > 0 ? httpsPort : StandardHttpsPort;

    public static bool IsMacOsDescription(string? osDescription)
    {
        if (string.IsNullOrWhiteSpace(osDescription))
            return false;

        var os = osDescription.ToLowerInvariant();
        return os.Contains("darwin", StringComparison.Ordinal)
            || os.Contains("macos", StringComparison.Ordinal)
            || os.Contains("osx", StringComparison.Ordinal)
            || os.Contains("mac os", StringComparison.Ordinal);
    }
}
