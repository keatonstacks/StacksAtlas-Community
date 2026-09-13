using System.Text.Json;
using Microsoft.Extensions.Logging;
using StacksAtlas.Core.Models.Updates;

namespace StacksAtlas.Core.Services.Updates;

/// <summary>Builds UpdateCheckResult payloads from Hub-staged depot manifests for enrolled Nodes.</summary>
public sealed class HubDepotUpdateCheckService(
    IUpdateDepotService depotService,
    ILogger<HubDepotUpdateCheckService> logger)
{
    private static readonly JsonSerializerOptions MetaJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <param name="currentVersion">Calling Node's running version (not the Hub's).</param>
    /// <param name="artifactKey">Calling Node's artifact key (e.g. win-x64-msi).</param>
    public UpdateCheckResult CheckForNode(
        string? channel,
        string currentVersion,
        string artifactKey,
        string? publicArtifactBaseUrl = null)
    {
        var resolvedChannel = UpdateDepotPaths.NormalizeChannel(channel);
        var version = (currentVersion ?? "").Trim();
        var key = (artifactKey ?? "").Trim();
        if (string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(key))
        {
            return Failure(
                version,
                resolvedChannel,
                key,
                UpdateCheckStatuses.CheckFailed,
                "Node version and artifactKey are required for Hub depot update check.");
        }

        if (!depotService.TryGetLatestStagedRelease(resolvedChannel, out var stagedVersion, out var manifest, out _))
        {
            return Failure(
                version,
                resolvedChannel,
                key,
                UpdateCheckStatuses.Unavailable,
                "No update package is staged on the Hub depot for this channel.");
        }

        if (!manifest.Artifacts.TryGetValue(key, out var artifact))
        {
            return Failure(
                version,
                resolvedChannel,
                key,
                UpdateCheckStatuses.Unavailable,
                "No update is staged for this platform in the Hub depot.");
        }

        string? downloadUrl = null;
        string? sha256 = artifact.Sha256;
        string? image = artifact.Image;
        string? digest = artifact.Digest;

        if (artifact.IsDockerArtifact)
        {
            if (depotService.TryResolveArtifactPath(resolvedChannel, stagedVersion, key, out var metaPath))
            {
                try
                {
                    var meta = JsonSerializer.Deserialize<DockerMeta>(File.ReadAllText(metaPath), MetaJsonOptions);
                    image ??= meta?.Image;
                    digest ??= meta?.Digest;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Hub depot: failed to read docker meta for {Version}", stagedVersion);
                }
            }
        }
        else if (!string.IsNullOrWhiteSpace(publicArtifactBaseUrl))
        {
            downloadUrl =
                $"{publicArtifactBaseUrl.TrimEnd('/')}/{Uri.EscapeDataString(key)}/{Uri.EscapeDataString(stagedVersion)}" +
                $"?channel={Uri.EscapeDataString(resolvedChannel)}";
        }
        else if (!string.IsNullOrWhiteSpace(artifact.Url))
        {
            downloadUrl = artifact.Url;
        }

        if (!string.IsNullOrWhiteSpace(artifact.MinUpgradeFrom))
        {
            if (!SemverUtility.TryCompare(version, artifact.MinUpgradeFrom, out var minCmp))
            {
                return Failure(
                    version,
                    resolvedChannel,
                    key,
                    UpdateCheckStatuses.CheckFailed,
                    $"Could not compare versions (current={version}, minUpgradeFrom={artifact.MinUpgradeFrom}).");
            }

            if (minCmp < 0)
            {
                return WithApplyHints(new UpdateCheckResult(
                    Status: UpdateCheckStatuses.CheckFailed,
                    UpdateAvailable: false,
                    CurrentVersion: version,
                    Channel: resolvedChannel,
                    ArtifactKey: key,
                    AvailableVersion: artifact.Version,
                    DownloadUrl: downloadUrl,
                    Sha256: sha256,
                    Image: image,
                    Digest: digest,
                    Criticality: artifact.Criticality,
                    ReleaseNotesUrl: manifest.ReleaseNotesUrl,
                    PublishedUtc: manifest.PublishedUtc,
                    Message: $"Current version {version} is below minimum {artifact.MinUpgradeFrom} for this release."));
            }
        }

        if (!SemverUtility.TryCompare(artifact.Version, version, out var comparison))
        {
            return Failure(
                version,
                resolvedChannel,
                key,
                UpdateCheckStatuses.CheckFailed,
                $"Could not compare versions (current={version}, staged={artifact.Version}).");
        }

        var updateAvailable = comparison > 0;
        var status = updateAvailable ? UpdateCheckStatuses.UpdateAvailable : UpdateCheckStatuses.UpToDate;
        var message = updateAvailable
            ? $"Update {artifact.Version} is available from the Hub depot."
            : comparison == 0
                ? $"You are running the latest Hub-staged release (v{version})."
                : $"You are on v{version}, ahead of the Hub-staged {resolvedChannel} release (v{artifact.Version}).";

        return WithApplyHints(new UpdateCheckResult(
            Status: status,
            UpdateAvailable: updateAvailable,
            CurrentVersion: version,
            Channel: resolvedChannel,
            ArtifactKey: key,
            AvailableVersion: artifact.Version,
            DownloadUrl: downloadUrl,
            Sha256: sha256,
            Image: image,
            Digest: digest,
            Criticality: artifact.Criticality,
            ReleaseNotesUrl: manifest.ReleaseNotesUrl,
            PublishedUtc: manifest.PublishedUtc,
            Message: message));
    }

    private static UpdateCheckResult Failure(
        string currentVersion,
        string channel,
        string artifactKey,
        string status,
        string message) =>
        WithApplyHints(new UpdateCheckResult(
            Status: status,
            UpdateAvailable: false,
            CurrentVersion: currentVersion,
            Channel: channel,
            ArtifactKey: artifactKey,
            AvailableVersion: null,
            DownloadUrl: null,
            Sha256: null,
            Image: null,
            Digest: null,
            Criticality: null,
            ReleaseNotesUrl: null,
            PublishedUtc: null,
            Message: message));

    private static UpdateCheckResult WithApplyHints(UpdateCheckResult result)
    {
        var (applySupported, applyMode) = UpdateApplyCapabilities.Resolve(result.ArtifactKey, result.DownloadUrl);
        return result with { ApplySupported = applySupported, ApplyMode = applyMode };
    }

    private sealed class DockerMeta
    {
        public string? Image { get; set; }
        public string? Digest { get; set; }
    }
}
