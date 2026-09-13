using System.Text.Json.Serialization;

namespace StacksAtlas.Core.Models.Updates;

/// <summary>
/// Signed release manifest published per channel (stable / preview).
/// Wire format uses camelCase keys; canonical bytes for signing sort keys lexicographically.
/// </summary>
public sealed class ReleaseManifest
{
    /// <summary>Schema version  -  increment only on breaking manifest shape changes.</summary>
    [JsonPropertyName("manifestVersion")]
    public int ManifestVersion { get; init; } = 1;

    [JsonPropertyName("channel")]
    public string Channel { get; init; } = "stable";

    [JsonPropertyName("publishedUtc")]
    public DateTime PublishedUtc { get; init; }

    [JsonPropertyName("releaseNotesUrl")]
    public string? ReleaseNotesUrl { get; init; }

    /// <summary>
    /// Platform artifact entries keyed by stable identifiers
    /// (e.g. win-x64-msi, win-x64-portable, osx-universal-dmg, linux-docker).
    /// </summary>
    [JsonPropertyName("artifacts")]
    public Dictionary<string, ReleaseArtifactEntry> Artifacts { get; init; } = new(StringComparer.Ordinal);
}
