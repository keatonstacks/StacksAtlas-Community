using StacksAtlas.Core.Helpers;

namespace StacksAtlas.Core.Services.Updates;

/// <summary>
/// Filesystem layout for the Hub update depot (1.9.2 Slice 2).
/// Root: {BaseDataDir}/updates/depot/{channel}/{version}/
/// </summary>
public static class UpdateDepotPaths
{
    public const string DepotFolderName = "depot";
    public const string UpdatesFolderName = "updates";
    public const string ManifestFileName = "manifest.json";
    public const string ManifestSignatureFileName = "manifest.json.sig";

    public static string GetDepotRoot(string? baseDataDir = null) =>
        Path.Combine(baseDataDir ?? PlatformPaths.BaseDataDir, UpdatesFolderName, DepotFolderName);

    public static string GetChannelDirectory(string channel, string? baseDataDir = null)
    {
        var normalized = NormalizeChannel(channel);
        return Path.Combine(GetDepotRoot(baseDataDir), normalized);
    }

    public static string GetVersionDirectory(string channel, string version, string? baseDataDir = null)
    {
        if (string.IsNullOrWhiteSpace(version))
            throw new ArgumentException("Version is required.", nameof(version));

        var safeVersion = version.Trim();
        return Path.Combine(GetChannelDirectory(channel, baseDataDir), safeVersion);
    }

    public static string NormalizeChannel(string? channel)
    {
        if (string.IsNullOrWhiteSpace(channel))
            return "stable";

        var trimmed = channel.Trim().ToLowerInvariant();
        return trimmed is "stable" or "preview" ? trimmed : "stable";
    }

    /// <summary>Highest semver version directory under a channel, ignoring hidden/.staging folders.</summary>
    public static string? TryFindLatestVersionDirectory(string channel, string? baseDataDir = null)
    {
        var channelDir = GetChannelDirectory(channel, baseDataDir);
        if (!Directory.Exists(channelDir))
            return null;

        string? bestDir = null;
        string? bestVersion = null;
        foreach (var dir in Directory.GetDirectories(channelDir))
        {
            var name = Path.GetFileName(dir);
            if (string.IsNullOrWhiteSpace(name) || name.StartsWith(".", StringComparison.Ordinal))
                continue;
            if (!SemverUtility.TryParse(name, out _))
                continue;
            if (bestVersion is null || SemverUtility.Compare(name, bestVersion) > 0)
            {
                bestVersion = name;
                bestDir = dir;
            }
        }

        return bestDir;
    }

    public static bool TryResolveArtifactPath(
        string channel,
        string version,
        string artifactKey,
        out string path,
        string? baseDataDir = null)
    {
        path = "";
        var fileName = UpdateDepotArtifactFiles.ResolveFileName(artifactKey);
        if (fileName is null || string.IsNullOrWhiteSpace(version))
            return false;

        var candidate = Path.Combine(GetVersionDirectory(channel, version.Trim(), baseDataDir), fileName);
        if (!File.Exists(candidate))
            return false;

        path = candidate;
        return true;
    }
}
