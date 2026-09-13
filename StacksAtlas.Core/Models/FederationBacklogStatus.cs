namespace StacksAtlas.Core.Models;

public record FederationBacklogStatus
{
    public bool HubConnected { get; init; }
    public bool DeepSleep { get; init; }
    public bool FullSyncRequired { get; init; }
    public DateTime? DisconnectedSinceUtc { get; init; }
    public int PendingTelemetryBatches { get; init; }
    public int PendingDeviceChanges { get; init; }
    public int PendingLogs { get; init; }
    public int PendingEvents { get; init; }
    public int PendingAuditEvents { get; init; }
    public int PendingAlerts { get; init; }
    public int MaxPendingDeviceChanges { get; init; }
    public string BacklogSeverity { get; init; } = "Info";
    public string Summary { get; init; } = string.Empty;
}
