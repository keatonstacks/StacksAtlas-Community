using System;

namespace StacksAtlas.Core.Models;

/// <summary>
/// DTO representing the current physical and operational health of the LiteDB engine.
/// </summary>
public class DatabaseHealth
{
    public long SizeBytes { get; set; }
    public long ApplianceSizeBytes { get; set; }
    public long FleetSizeBytes { get; set; }
    public bool HasFleetDatabase { get; set; }
    public string ApplianceEngine { get; set; } = "LiteDB";
    public string? FleetEngine { get; set; }
    public int RetentionDays { get; set; }
    public int CleanupIntervalMinutes { get; set; }
    public DateTime LastCleanupUtc { get; set; }
    public DateTime NextCleanupUtc { get; set; }
    public DateTime LastCompactUtc { get; set; }
    public DateTime NextCompactUtc { get; set; }
    public int LastPurgedCount { get; set; }
    public string CurrentStatus { get; set; } = "Idle";
    public string? LastErrorMessage { get; set; }
}
