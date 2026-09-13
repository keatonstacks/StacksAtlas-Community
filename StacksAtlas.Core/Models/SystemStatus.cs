namespace StacksAtlas.Core.Models;

/// <summary>
/// Represents the current health and operational state of the StacksAtlas engine.
/// </summary>
public record SystemStatus
{
    public string Status { get; init; } = "Online";
    public string EnginePhase { get; init; } = "Idle";
    public bool IsScanning { get; init; }
    public bool DatabaseReady { get; init; }
    public DateTime LastSweepTime { get; init; }
    public string Version { get; init; } = "1.0.0";
    public string? HostIp { get; init; }
    public string? ActiveInterface { get; init; }
    public string? ScanningRange { get; init; }
    public List<string> MissingDependencies { get; init; } = new();
}
