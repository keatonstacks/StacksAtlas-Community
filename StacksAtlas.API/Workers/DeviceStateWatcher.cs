using LiteDB;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StacksAtlas.Core.Data;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.State;
using StacksAtlas.Core.Services.Federation;

namespace StacksAtlas.API.Workers;

/// <summary>
/// Background worker active on Nodes. 
/// Monitors the local LiteDB for device changes and hands them to FederationSyncWorker.
/// </summary>
public class DeviceStateWatcher : BackgroundService
{
    private readonly ILogger<DeviceStateWatcher> _logger;
    private readonly IDeviceRepository _deviceRepo;
    private readonly LiteDatabase _db;
    private readonly SystemStateProvider _stateProvider;
    private readonly IFederationBacklogService _backlogService;
    private DateTime _lastSyncUtc = DateTime.MinValue;

    public DeviceStateWatcher(
        ILogger<DeviceStateWatcher> logger,
        IDeviceRepository deviceRepo,
        LiteDatabase db,
        SystemStateProvider stateProvider,
        IFederationBacklogService backlogService)
    {
        _logger = logger;
        _deviceRepo = deviceRepo;
        _db = db;
        _stateProvider = stateProvider;
        _backlogService = backlogService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!ExecutionState.IsFederationActive) return;

        _logger.LogInformation("DeviceStateWatcher: Waiting for database to be ready...");
        while (!_stateProvider.IsDatabaseReady && !stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(1000, stoppingToken);
        }

        if (stoppingToken.IsCancellationRequested) return;

        _logger.LogInformation("DeviceStateWatcher: Initializing...");

        // Load last sync time from DB
        var stateCol = _db.GetCollection<FederationState>("federation_state");
        var state = stateCol.FindById("singleton");
        if (state != null)
        {
            _lastSyncUtc = state.LastSyncUtc;
            _logger.LogInformation("DeviceStateWatcher: Resuming from last sync: {LastSync}", _lastSyncUtc);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DetectAndPushChangesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DeviceStateWatcher: Error during change detection.");
            }

            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }

    private async Task DetectAndPushChangesAsync(CancellationToken token)
    {
        // Refresh sync state from DB (in case FederationSyncWorker updated it)
        var stateCol = _db.GetCollection<FederationState>("federation_state");
        var state = stateCol.FindById("singleton");
        if (state != null) _lastSyncUtc = state.LastSyncUtc;

        // Query for devices modified since last sync
        // Note: LiteDB Query() is not async, so we wrap it or just run it as is since it's a local DB
        var changedDevices = _deviceRepo.GetAll(includeDeleted: true, includePermanentlyRemoved: true)
            .Where(x => x.LastModifiedUtc > _lastSyncUtc)
            .OrderBy(x => x.LastModifiedUtc)
            .ToList();

        if (changedDevices.Count > 0)
        {
            _logger.LogDebug("DeviceStateWatcher: Detected {Count} changed devices.", changedDevices.Count);

            const int BatchSize = 50;
            var queuedAny = false;
            for (int i = 0; i < changedDevices.Count; i += BatchSize)
            {
                var batch = changedDevices.Skip(i).Take(BatchSize).ToList();
                if (_backlogService.TryQueueTelemetryBatch(batch))
                {
                    queuedAny = true;
                }
                else
                {
                    _logger.LogWarning(
                        "DeviceStateWatcher: Skipped queueing {Count} device changes because federation backlog requires snapshot sync on reconnect.",
                        batch.Count);
                }
            }

            if (!queuedAny)
                return;

            // Advance sync cursor only when changes were queued for Hub delivery.
            _lastSyncUtc = changedDevices.Max(x => x.LastModifiedUtc);
            
            state ??= new FederationState();
            state.LastSyncUtc = _lastSyncUtc;
            stateCol.Upsert(state);
            
            _logger.LogDebug("DeviceStateWatcher: Advanced sync cursor to {LastSync}", _lastSyncUtc);
        }
    }
}
