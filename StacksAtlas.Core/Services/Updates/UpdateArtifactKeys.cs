namespace StacksAtlas.Core.Services.Updates;

/// <summary>Stable manifest artifact identifiers (sorted lexicographically when canonicalized).</summary>
public static class UpdateArtifactKeys
{
    public const string LinuxDocker = "linux-docker";
    public const string OsxUniversalDmg = "osx-universal-dmg";
    public const string WinX64Msi = "win-x64-msi";
    public const string WinX64Portable = "win-x64-portable";

    public static string ResolveForRuntime(bool isPortable)
    {
        if (OperatingSystem.IsWindows())
            return isPortable ? WinX64Portable : WinX64Msi;
        if (OperatingSystem.IsMacOS())
            return OsxUniversalDmg;
        if (OperatingSystem.IsLinux())
            return LinuxDocker;

        return WinX64Msi;
    }
}
