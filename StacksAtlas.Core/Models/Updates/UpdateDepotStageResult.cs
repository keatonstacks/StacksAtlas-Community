namespace StacksAtlas.Core.Models.Updates;

/// <summary>Result of Hub <c>POST /api/system/updates/stage</c>.</summary>
public sealed class UpdateDepotStageResult
{
    public required bool Success { get; init; }
    public required string Channel { get; init; }
    public string? Version { get; init; }
    public required string Message { get; init; }
    public string? Path { get; init; }
    public required IReadOnlyList<string> StagedArtifactKeys { get; init; }
    public DateTime CompletedUtc { get; init; }

    /// <summary>True when the channel already had this release version staged (artifacts refreshed).</summary>
    public bool AlreadyStaged { get; init; }
}
