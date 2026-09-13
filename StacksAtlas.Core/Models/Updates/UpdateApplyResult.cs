namespace StacksAtlas.Core.Models.Updates;

public sealed record UpdateApplyResult(
    string Status,
    string Message,
    string? TargetVersion,
    string? Channel,
    string? SnapshotApplianceFileName,
    string? SnapshotFleetFileName,
    bool RestartRequired);
