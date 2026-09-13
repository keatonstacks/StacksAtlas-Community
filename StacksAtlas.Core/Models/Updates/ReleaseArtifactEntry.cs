using System.Text.Json.Serialization;

namespace StacksAtlas.Core.Models.Updates;

/// <summary>
/// One distributable artifact in a release manifest.
/// File artifacts use url + sha256; container artifacts use image + digest.
/// </summary>
public sealed class ReleaseArtifactEntry
{
    [JsonPropertyName("version")]
    public string Version { get; init; } = "";

    /// <summary>HTTPS download URL (file artifacts).</summary>
    [JsonPropertyName("url")]
    public string? Url { get; init; }

    /// <summary>Lowercase hex SHA-256 of the file (file artifacts).</summary>
    [JsonPropertyName("sha256")]
    public string? Sha256 { get; init; }

    [JsonPropertyName("sizeBytes")]
    public long? SizeBytes { get; init; }

    /// <summary>Minimum appliance version allowed to apply this update (semver).</summary>
    [JsonPropertyName("minUpgradeFrom")]
    public string? MinUpgradeFrom { get; init; }

    /// <summary>recommended | critical</summary>
    [JsonPropertyName("criticality")]
    public string? Criticality { get; init; }

    /// <summary>Container image reference (docker artifacts).</summary>
    [JsonPropertyName("image")]
    public string? Image { get; init; }

    /// <summary>Image digest sha256:... (docker artifacts).</summary>
    [JsonPropertyName("digest")]
    public string? Digest { get; init; }

    public bool IsDockerArtifact =>
        !string.IsNullOrWhiteSpace(Image) || !string.IsNullOrWhiteSpace(Digest);

    public bool IsFileArtifact =>
        !string.IsNullOrWhiteSpace(Url) || !string.IsNullOrWhiteSpace(Sha256);
}
