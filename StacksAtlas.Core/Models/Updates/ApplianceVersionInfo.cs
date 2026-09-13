namespace StacksAtlas.Core.Models.Updates;

/// <summary>Authoritative appliance version reported by the API (replaces UI-only constants).</summary>
public sealed record ApplianceVersionInfo(
    string Version,
    string Channel,
    string InstallMode,
    bool IsPortable,
    string ArtifactKey,
    DateTime ReportedUtc);
