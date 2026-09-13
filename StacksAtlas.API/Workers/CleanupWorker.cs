using System.Diagnostics;
using LiteDB;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StacksAtlas.Core.Data;
using StacksAtlas.Core.Database;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Settings;

namespace StacksAtlas.API.Workers;

public sealed class CleanupWorker(
    ILogger<CleanupWorker> logger,
    IServiceScopeFactory scopeFactory,
    LiteDatabase db,
    StacksAtlas.Core.Abstractions.IClock clock,
    CleanupSettingsStore settingsStore) : BackgroundService
{
    private readonly ILogger<CleanupWorker> _logger = logger;
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly LiteDatabase _db = db;
    private readonly StacksAtlas.Core.Abstractions.IClock _clock = clock;
    private readonly CleanupSettingsStore _settingsStore = settingsStore;

    // Sweep retention window (Aggressive pruning: 1 hour)
    private static readonly TimeSpan SweepRetention = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _logger.LogInformation("CleanupWorker starting...");

            // --- STARTUP VACUUM CHECK ---
            await VacuumIfBloated(stoppingToken);

            // Initial startup delay
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                var options = _settingsStore.Current;
                if (options.Enabled)
                {
                    using var scope = _scopeFactory.CreateScope();
                    var maintenance = scope.ServiceProvider.GetRequiredService<IDatabaseMaintenance>();
                    var deviceRepo = scope.ServiceProvider.GetRequiredService<IDeviceRepository>();
                    var sweepRepo = scope.ServiceProvider.GetRequiredService<SweepHistoryRepository>();

                    var cleanupStart = _clock.UtcNow;
                    var timer = Stopwatch.StartNew();
                    var state = LoadState();

                    state.CurrentStatus = "Cleaning";
                    state.LastErrorMessage = null;
                    SaveState(state);

                    int removed = 0;
                    try
                    {
                        removed = await maintenance.CleanupAsync(stoppingToken);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "CleanupAsync failed");
                        state.LastErrorMessage = "Cleanup failed: " + ex.Message;
                    }

                    // 2. Sweep History Pruning
                    try
                    {
                        var cutoff = _clock.UtcNow - SweepRetention;
                        sweepRepo.PruneOlderThan(cutoff);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Sweep history pruning failed");
                        state.LastErrorMessage = (state.LastErrorMessage == null ? "" : state.LastErrorMessage + " | ") + "Sweep history pruning failed: " + ex.Message;
                    }

                    timer.Stop();
                    state.LastCleanupUtc = cleanupStart;
                    state.NextCleanupUtc = cleanupStart.AddMinutes(options.RunEveryMinutes);
                    state.LastPurgedCount = removed;
                    state.LastDatabaseSize = await maintenance.GetDatabaseSizeBytesAsync();
                    state.CurrentStatus = "Idle";
                    SaveState(state);

                    // 3. Weekly Compaction
                    if (options.CompactWeekly && _clock.UtcNow >= state.NextCompactUtc)
                    {
                        await RunCompactionAsync(maintenance, state, stoppingToken);
                        SaveState(state);
                    }
                }

                await DelayNextRun(_settingsStore.Current, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("CleanupWorker stopping.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception in CleanupWorker");
        }
    }

    private static async Task DelayNextRun(TimeSpan duration, CancellationToken token)
        => await Task.Delay(duration, token);

    private async Task DelayNextRun(CleanupOptions options, CancellationToken token)
        => await Task.Delay(TimeSpan.FromMinutes(options.RunEveryMinutes), token);

    private async Task RunCompactionAsync(IDatabaseMaintenance maintenance, MaintenanceState state, CancellationToken token)
    {
        var timer = Stopwatch.StartNew();
        try
        {
            state.CurrentStatus = "Compacting";
            SaveState(state);

            if (_logger.IsEnabled(LogLevel.Debug))
                _logger.LogDebug("Starting LiteDB compaction...");

            // Note: LiteDB Rebuild usually cannot be cancelled mid-operation safely
            // but we await it here so at least the task structure is correct.
            await maintenance.CompactAsync(token);

            var end = _clock.UtcNow;
            state.LastCompactUtc = end;
            state.NextCompactUtc = end.AddDays(7);
            state.LastDatabaseSize = await maintenance.GetDatabaseSizeBytesAsync();
            state.CurrentStatus = "Idle";

            if (_logger.IsEnabled(LogLevel.Debug))
                _logger.LogDebug("Compaction completed. Duration={Ms}ms", timer.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CompactAsync failed");
            state.CurrentStatus = "Idle";
            state.LastErrorMessage = "Compaction failed: " + ex.Message;
        }
    }

    private MaintenanceState LoadState()
    {
        var col = _db.GetCollection<MaintenanceState>("maintenance");
        return col.FindById(1) ?? new MaintenanceState { Id = 1, NextCompactUtc = _clock.UtcNow };
    }

    private void SaveState(MaintenanceState state)
    {
        var col = _db.GetCollection<MaintenanceState>("maintenance");
        col.Upsert(state);
    }

    private async Task VacuumIfBloated(CancellationToken token)
    {
        try
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                var maintenance = scope.ServiceProvider.GetRequiredService<IDatabaseMaintenance>();
                long size = await maintenance.GetDatabaseSizeBytesAsync();

                if (size > 100 * 1024 * 1024) // 100 MB
                {
                    _logger.LogWarning("Startup Detect: Database Size is {SizeMB} MB. Initiating immediate emergency compaction...", size / 1024 / 1024);
                    // Force a prune first
                    var sweepRepo = scope.ServiceProvider.GetRequiredService<SweepHistoryRepository>();
                    sweepRepo.PruneOlderThan(_clock.UtcNow - SweepRetention);
                    
                    // Then compact
                    await maintenance.CompactAsync(token);
                    _logger.LogInformation("Emergency compaction complete.");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to perform startup vacuum.");
        }
    }
}
