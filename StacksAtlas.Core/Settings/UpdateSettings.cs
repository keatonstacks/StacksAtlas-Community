namespace StacksAtlas.Core.Settings;

/// <summary>Opt-in release manifest endpoints  -  appliances only fetch these configured URLs (no caller-supplied URLs).</summary>
public sealed class UpdateSettings
{
    public const string SectionName = "StacksAtlas:Updates";

    public string DefaultChannel { get; set; } = "stable";

    public string StableManifestUrl { get; set; } = "https://releases.stacksatlas.com/stable/manifest.json";

    public string StableSignatureUrl { get; set; } = "https://releases.stacksatlas.com/stable/manifest.json.sig";

    public string PreviewManifestUrl { get; set; } = "https://releases.stacksatlas.com/preview/manifest.json";

    public string PreviewSignatureUrl { get; set; } = "https://releases.stacksatlas.com/preview/manifest.json.sig";

    public (string ManifestUrl, string SignatureUrl) ResolveEndpoints(string? channel)
    {
        var normalized = string.IsNullOrWhiteSpace(channel)
            ? DefaultChannel
            : channel.Trim().ToLowerInvariant();

        return normalized switch
        {
            "stable" => (StableManifestUrl, StableSignatureUrl),
            "preview" => (PreviewManifestUrl, PreviewSignatureUrl),
            _ => throw new ArgumentException($"Unsupported update channel: {channel}")
        };
    }
}
