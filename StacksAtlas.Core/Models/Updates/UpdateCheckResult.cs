namespace StacksAtlas.Core.Models.Updates;

/// <summary>Result of an opt-in update check  -  no device inventory leaves the appliance.</summary>
public sealed record UpdateCheckResult(
    string Status,
    bool UpdateAvailable,
    string CurrentVersion,
    string Channel,
    string ArtifactKey,
    string? AvailableVersion,
    string? DownloadUrl,
    string? Sha256,
    string? Image,
    string? Digest,
    string? Criticality,
    string? ReleaseNotesUrl,
    DateTime? PublishedUtc,
    string? Message,
    bool ApplySupported = false,
    string? ApplyMode = null);
