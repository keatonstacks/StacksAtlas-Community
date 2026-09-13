namespace StacksAtlas.Core.Models.Updates;

/// <summary>Hub update depot inventory (read-only status for Slice 2).</summary>
public sealed class UpdateDepotStatus
{
    public required string DepotRoot { get; init; }
    public required IReadOnlyList<UpdateDepotChannelStatus> Channels { get; init; }
    public DateTime QueriedUtc { get; init; }
}

public sealed class UpdateDepotChannelStatus
{
    public required string Channel { get; init; }
    public required IReadOnlyList<UpdateDepotVersionEntry> Versions { get; init; }
}

public sealed class UpdateDepotVersionEntry
{
    public required string Version { get; init; }
    public required string Path { get; init; }
    public bool HasManifest { get; init; }
    public bool HasSignature { get; init; }
    public DateTime? PublishedUtc { get; init; }
    public required IReadOnlyList<UpdateDepotArtifactEntry> Artifacts { get; init; }
    public long TotalBytes { get; init; }
}

public sealed class UpdateDepotArtifactEntry
{
    public required string FileName { get; init; }
    public long SizeBytes { get; init; }
}
