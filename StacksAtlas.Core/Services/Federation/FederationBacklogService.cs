using LiteDB;
using Microsoft.Extensions.Logging;
using StacksAtlas.Core.Abstractions;
using StacksAtlas.Core.Models;

namespace StacksAtlas.Core.Services.Federation;

public interface IFederationBacklogService
{
    const int MaxPendingDeviceChanges = 50_000;
    static readonly TimeSpan SnapshotDisconnectThreshold = TimeSpan.FromMinutes(15);
    static readonly TimeSpan AlertBackfillWindow = TimeSpan.FromDays(30);

    void EnsureMigrationComplete();
    FederationBacklogStatus GetStatus(bool hubConnected, bool deepSleep);
    bool TryQueueTelemetryBatch(List<Device> devices);
    bool TryQueueLifecycleBatch(List<Device> devices);
    bool ShouldCatchUpOnReconnect();
    void RequestFullDeviceSync(string reason);
    void PrepareDeviceSnapshotOnReconnect();
    void MarkHubConnected();
    void MarkHubDisconnected();
    void ClearDisconnectedState();
    void ClearPendingTelemetry();
    List<AlertEvent> GetAlertsForBackfill();
    List<SystemEvent> GetAnomaliesForBackfill();
    FederationState GetState();
}

public sealed class FederationBacklogService(
    LiteDatabase db,
    IClock clock,
    ILogger<FederationBacklogService> logger) : IFederationBacklogService
{
    private readonly LiteDatabase _db = db;
    private readonly IClock _clock = clock;
    private readonly ILogger<FederationBacklogService> _logger = logger;

    public void EnsureMigrationComplete()
    {
        var state = GetOrCreateState();
        if (state.BacklogMigrationComplete)
            return;

        ClearPendingTelemetry();
        state.LastSyncUtc = DateTime.MinValue;
        state.FullSyncRequired = true;
        state.BacklogMigrationComplete = true;
        SaveState(state);

        _logger.LogWarning(
            "Federation backlog migration applied: pending device telemetry trimmed and full device snapshot scheduled for next Hub connection.");
    }

    public FederationBacklogStatus GetStatus(bool hubConnected, bool deepSleep)
    {
        var state = GetOrCreateState();
        var pendingTelemetry = _db.GetCollection<PendingTelemetry>("pending_telemetry");
        var pendingLogs = _db.GetCollection<PendingLog>("pending_logs");
        var pendingEvents = _db.GetCollection<PendingEvent>("pending_events");
        var pendingAuditEvents = _db.GetCollection<PendingAuditEvent>("pending_audit_events");
        var pendingAlerts = _db.GetCollection<PendingAlert>("pending_alerts");

        var batches = pendingTelemetry.FindAll().ToList();
        var pendingDeviceChanges = batches.Sum(b => b.Devices.Count);

        var severity = "Info";
        if (state.FullSyncRequired || pendingDeviceChanges >= IFederationBacklogService.MaxPendingDeviceChanges)
            severity = "Warning";
        if (!hubConnected && state.HubDisconnectedSinceUtc.HasValue &&
            _clock.UtcNow - state.HubDisconnectedSinceUtc.Value > TimeSpan.FromHours(24))
            severity = "Warning";

        var summary = hubConnected
            ? "Hub connection active. Federation backlog is draining."
            : state.FullSyncRequired
                ? "Hub offline. Device snapshot sync scheduled for next reconnect."
                : deepSleep
                    ? "Hub offline. Node is in deep sleep; local monitoring remains active."
                    : "Hub offline. Federation backlog is accumulating locally; local monitoring remains active.";

        return new FederationBacklogStatus
        {
            HubConnected = hubConnected,
            DeepSleep = deepSleep,
            FullSyncRequired = state.FullSyncRequired,
            DisconnectedSinceUtc = state.HubDisconnectedSinceUtc,
            PendingTelemetryBatches = batches.Count,
            PendingDeviceChanges = pendingDeviceChanges,
            PendingLogs = pendingLogs.Count(),
            PendingEvents = pendingEvents.Count(),
            PendingAuditEvents = pendingAuditEvents.Count(),
            PendingAlerts = pendingAlerts.Count(),
            MaxPendingDeviceChanges = IFederationBacklogService.MaxPendingDeviceChanges,
            BacklogSeverity = severity,
            Summary = summary
        };
    }

    public bool TryQueueTelemetryBatch(List<Device> devices)
    {
        if (devices.Count == 0)
            return true;

        var state = GetOrCreateState();
        if (state.FullSyncRequired)
            return false;

        var pendingCol = _db.GetCollection<PendingTelemetry>("pending_telemetry");
        var currentCount = pendingCol.FindAll().Sum(p => p.Devices.Count);
        if (currentCount + devices.Count > IFederationBacklogService.MaxPendingDeviceChanges)
        {
            RequestFullDeviceSync("Pending device telemetry exceeded ceiling");
            return false;
        }

        pendingCol.Insert(new PendingTelemetry { Devices = devices, CreatedAtUtc = _clock.UtcNow });
        return true;
    }

    public bool TryQueueLifecycleBatch(List<Device> devices)
    {
        if (devices.Count == 0)
            return true;

        // Lifecycle governance must reach the Hub even when a full snapshot is pending.
        var pendingCol = _db.GetCollection<PendingTelemetry>("pending_telemetry");
        pendingCol.Insert(new PendingTelemetry { Devices = devices, CreatedAtUtc = _clock.UtcNow });
        return true;
    }

    public bool ShouldCatchUpOnReconnect()
    {
        var state = GetOrCreateState();
        if (state.FullSyncRequired)
            return true;

        if (state.HubDisconnectedSinceUtc.HasValue &&
            _clock.UtcNow - state.HubDisconnectedSinceUtc.Value >= IFederationBacklogService.SnapshotDisconnectThreshold)
            return true;

        var pendingDeviceChanges = _db.GetCollection<PendingTelemetry>("pending_telemetry")
            .FindAll()
            .Sum(p => p.Devices.Count);

        return pendingDeviceChanges >= IFederationBacklogService.MaxPendingDeviceChanges;
    }

    public void RequestFullDeviceSync(string reason)
    {
        var state = GetOrCreateState();
        state.FullSyncRequired = true;
        state.LastSyncUtc = DateTime.MinValue;
        SaveState(state);
        ClearPendingTelemetry();
        _logger.LogWarning("Federation backlog requested full device snapshot: {Reason}", reason);
    }

    public void PrepareDeviceSnapshotOnReconnect()
    {
        var state = GetOrCreateState();
        state.FullSyncRequired = false;
        state.LastSyncUtc = DateTime.MinValue;
        SaveState(state);
        ClearPendingTelemetry();
        _logger.LogInformation("Federation backlog prepared device snapshot for Hub reconnect.");
    }

    public void MarkHubConnected()
    {
        var state = GetOrCreateState();
        state.LastSuccessfulHubPushUtc = _clock.UtcNow;
        state.HubDisconnectedSinceUtc = null;
        SaveState(state);
    }

    public void ClearDisconnectedState()
    {
        var state = GetOrCreateState();
        state.HubDisconnectedSinceUtc = null;
        SaveState(state);
    }

    public void MarkHubDisconnected()
    {
        var state = GetOrCreateState();
        state.HubDisconnectedSinceUtc ??= _clock.UtcNow;
        SaveState(state);
    }

    public void ClearPendingTelemetry()
    {
        _db.GetCollection<PendingTelemetry>("pending_telemetry").DeleteAll();
    }

    public List<AlertEvent> GetAlertsForBackfill()
    {
        var cutoff = _clock.UtcNow - IFederationBacklogService.AlertBackfillWindow;
        return _db.GetCollection<AlertEvent>("alert_events")
            .Query()
            .Where(x => x.TriggeredAt >= cutoff)
            .OrderBy(x => x.TriggeredAt)
            .ToList();
    }

    public List<SystemEvent> GetAnomaliesForBackfill()
    {
        var cutoff = _clock.UtcNow - IFederationBacklogService.AlertBackfillWindow;
        return _db.GetCollection<SystemEvent>("events")
            .Query()
            .Where(x => x.Timestamp >= cutoff && (x.Severity == "Warning" || x.Severity == "Critical"))
            .OrderBy(x => x.Timestamp)
            .ToList();
    }

    public FederationState GetState() => GetOrCreateState();

    private FederationState GetOrCreateState()
    {
        var col = _db.GetCollection<FederationState>("federation_state");
        return col.FindById("singleton") ?? new FederationState();
    }

    private void SaveState(FederationState state)
    {
        var col = _db.GetCollection<FederationState>("federation_state");
        col.Upsert(state);
    }
}
