namespace StacksAtlas.Core.Services.Updates;

public static class UpdateApplyCapabilities
{
    public const string ApplyModeInApp = "inApp";
    public const string ApplyModeGuided = "guided";
    public const string ApplyModeManual = "manual";

    public static (bool ApplySupported, string ApplyMode) Resolve(string artifactKey, string? downloadUrl)
    {
        // Docker Nodes always use host-side pull/recreate (any OS). Image/digest from the
        // check response is enough; a file download URL is not required.
        if (string.Equals(artifactKey, UpdateArtifactKeys.LinuxDocker, StringComparison.Ordinal))
            return (false, ApplyModeGuided);

        if (string.IsNullOrWhiteSpace(downloadUrl))
            return (false, ApplyModeManual);

        if (!OperatingSystem.IsWindows())
            return (false, ApplyModeManual);

        return artifactKey switch
        {
            UpdateArtifactKeys.WinX64Msi => (true, ApplyModeInApp),
            UpdateArtifactKeys.WinX64Portable => (true, ApplyModeInApp),
            UpdateArtifactKeys.OsxUniversalDmg => (false, ApplyModeManual),
            _ => (false, ApplyModeManual)
        };
    }
}
