namespace StacksAtlas.Core.Services.Updates;

/// <summary>Stable on-disk file names for Hub depot artifacts.</summary>
public static class UpdateDepotArtifactFiles
{
    public const string LinuxDockerMetaFileName = "linux-docker.meta.json";

    public static string? ResolveFileName(string artifactKey) =>
        artifactKey switch
        {
            UpdateArtifactKeys.WinX64Msi => "StacksAtlas.msi",
            UpdateArtifactKeys.WinX64Portable => "StacksAtlas-Portable.exe",
            UpdateArtifactKeys.OsxUniversalDmg => "StacksAtlas.dmg",
            UpdateArtifactKeys.LinuxDocker => LinuxDockerMetaFileName,
            _ => null
        };
}
