using StacksAtlas.Core.Models.Updates;

namespace StacksAtlas.Core.Services.Updates;

public interface IUpdateDepotService
{
    /// <summary>Scan Hub depot filesystem. Empty when root missing or no staged versions.</summary>
    UpdateDepotStatus GetStatus();

    /// <summary>
    /// Fetch signed channel manifest from CDN, verify, download file artifacts into the Hub depot.
    /// Docker artifacts are recorded as metadata sidecars (image/digest) without pulling the image.
    /// </summary>
    Task<UpdateDepotStageResult> StageAsync(string? channel = null, CancellationToken cancellationToken = default);

    /// <summary>Latest staged release for a channel (manifest already verified at stage time).</summary>
    bool TryGetLatestStagedRelease(string? channel, out string version, out ReleaseManifest manifest, out string versionDirectory);

    /// <summary>Resolve a staged artifact file path for Node download.</summary>
    bool TryResolveArtifactPath(string? channel, string version, string artifactKey, out string path);
}
