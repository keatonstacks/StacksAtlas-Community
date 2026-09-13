using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StacksAtlas.Core.Data;
using StacksAtlas.Core.Data.Hub;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Services.Audit;
using StacksAtlas.API.Hubs;
using StacksAtlas.Core.Services.Governance;

namespace StacksAtlas.API.Services;

/// <summary>
/// A dedicated background service that drains the FederatedIngestionBuffer
/// channels and writes them to the database in highly optimized bulk batches.
/// </summary>
public class FederatedIngestionWorker : BackgroundService
{
    private readonly FederatedIngestionBuffer _buffer;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FederatedIngestionWorker> _logger;

    public FederatedIngestionWorker(
        FederatedIngestionBuffer buffer,
        IServiceScopeFactory scopeFactory,
        ILogger<FederatedIngestionWorker> logger)
    {
        _buffer = buffer;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("FederatedIngestionWorker: High-throughput background processor starting.");

        // Run all telemetry, log, system event, and alert event ingestion pipelines concurrently
        await Task.WhenAll(
            ProcessDevicesAsync(stoppingToken),
            ProcessLogsAsync(stoppingToken),
            ProcessSystemEventsAsync(stoppingToken),
            ProcessAlertEventsAsync(stoppingToken),
            ProcessAuditEventsAsync(stoppingToken),
            ProcessNodeHeartbeatsAsync(stoppingToken)
        );

        _logger.LogInformation("FederatedIngestionWorker: Background processor stopped.");
    }

    private async Task ProcessDevicesAsync(CancellationToken stoppingToken)
    {
        var batch = new List<Device>();
        var lastFlush = DateTime.UtcNow;
        const int maxBatchSize = 250;
        const int maxWaitMs = 1000; // 1 second max wait before flushing

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_buffer.IsPaused)
                {
                    await Task.Delay(200, stoppingToken);
                    continue;
                }

                // Drain any items currently in the channel buffer
                while (batch.Count < maxBatchSize && _buffer.DeviceReader.TryRead(out var device))
                {
                    batch.Add(device);
                }

                // Determine if we should flush the current batch
                var timeSinceLastFlush = DateTime.UtcNow - lastFlush;
                if (batch.Count > 0 && (batch.Count >= maxBatchSize || timeSinceLastFlush.TotalMilliseconds >= maxWaitMs))
                {
                    await FlushDevicesAsync(batch);
                    batch.Clear();
                    lastFlush = DateTime.UtcNow;
                }

                // If the batch is empty, wait asynchronously until a new item is pushed
                if (batch.Count == 0)
                {
                    await _buffer.DeviceReader.WaitToReadAsync(stoppingToken);
                }
                else
                {
                    // If we have partial items but haven't hit the timeout yet, yield briefly
                    await Task.Delay(100, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FederatedIngestionWorker: Error processing buffered devices.");
                await Task.Delay(2000, stoppingToken); // Settle down if database error occurs
            }
        }

        // Flush any remaining items on shutdown
        try
        {
            while (_buffer.DeviceReader.TryRead(out var device))
            {
                batch.Add(device);
            }

            if (batch.Count > 0)
            {
                _logger.LogInformation("FederatedIngestionWorker: Flushing final {Count} devices during shutdown...", batch.Count);
                await FlushDevicesAsync(batch);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FederatedIngestionWorker: Error flushing remaining devices during shutdown.");
        }
    }

    private async Task ProcessLogsAsync(CancellationToken stoppingToken)
    {
        var batch = new List<FederatedLog>();
        var lastFlush = DateTime.UtcNow;
        const int maxBatchSize = 500;
        const int maxWaitMs = 1500; // 1.5 seconds max wait before flushing

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_buffer.IsPaused)
                {
                    await Task.Delay(200, stoppingToken);
                    continue;
                }

                // Drain any items currently in the channel buffer
                while (batch.Count < maxBatchSize && _buffer.LogReader.TryRead(out var log))
                {
                    batch.Add(log);
                }

                // Determine if we should flush the current batch
                var timeSinceLastFlush = DateTime.UtcNow - lastFlush;
                if (batch.Count > 0 && (batch.Count >= maxBatchSize || timeSinceLastFlush.TotalMilliseconds >= maxWaitMs))
                {
                    await FlushLogsAsync(batch);
                    batch.Clear();
                    lastFlush = DateTime.UtcNow;
                }

                // If the batch is empty, wait asynchronously until a new item is pushed
                if (batch.Count == 0)
                {
                    await _buffer.LogReader.WaitToReadAsync(stoppingToken);
                }
                else
                {
                    // Yield briefly to gather more logs
                    await Task.Delay(100, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FederatedIngestionWorker: Error processing buffered logs.");
                await Task.Delay(2000, stoppingToken);
            }
        }

        // Flush any remaining items on shutdown
        try
        {
            while (_buffer.LogReader.TryRead(out var log))
            {
                batch.Add(log);
            }

            if (batch.Count > 0)
            {
                _logger.LogInformation("FederatedIngestionWorker: Flushing final {Count} logs during shutdown...", batch.Count);
                await FlushLogsAsync(batch);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FederatedIngestionWorker: Error flushing remaining logs during shutdown.");
        }
    }

    private async Task ProcessSystemEventsAsync(CancellationToken stoppingToken)
    {
        var batch = new List<SystemEvent>();
        var lastFlush = DateTime.UtcNow;
        const int maxBatchSize = 100;
        const int maxWaitMs = 1000;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_buffer.IsPaused)
                {
                    await Task.Delay(200, stoppingToken);
                    continue;
                }

                while (batch.Count < maxBatchSize && _buffer.SystemEventReader.TryRead(out var evt))
                {
                    batch.Add(evt);
                }

                var timeSinceLastFlush = DateTime.UtcNow - lastFlush;
                if (batch.Count > 0 && (batch.Count >= maxBatchSize || timeSinceLastFlush.TotalMilliseconds >= maxWaitMs))
                {
                    await FlushSystemEventsAsync(batch);
                    batch.Clear();
                    lastFlush = DateTime.UtcNow;
                }

                if (batch.Count == 0)
                {
                    await _buffer.SystemEventReader.WaitToReadAsync(stoppingToken);
                }
                else
                {
                    await Task.Delay(100, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FederatedIngestionWorker: Error processing buffered system events.");
                await Task.Delay(2000, stoppingToken);
            }
        }

        try
        {
            while (_buffer.SystemEventReader.TryRead(out var evt))
            {
                batch.Add(evt);
            }

            if (batch.Count > 0)
            {
                _logger.LogInformation("FederatedIngestionWorker: Flushing final {Count} system events during shutdown...", batch.Count);
                await FlushSystemEventsAsync(batch);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FederatedIngestionWorker: Error flushing remaining system events during shutdown.");
        }
    }

    private async Task ProcessAlertEventsAsync(CancellationToken stoppingToken)
    {
        var batch = new List<AlertEvent>();
        var lastFlush = DateTime.UtcNow;
        const int maxBatchSize = 100;
        const int maxWaitMs = 1000;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_buffer.IsPaused)
                {
                    await Task.Delay(200, stoppingToken);
                    continue;
                }

                while (batch.Count < maxBatchSize && _buffer.AlertEventReader.TryRead(out var evt))
                {
                    batch.Add(evt);
                }

                var timeSinceLastFlush = DateTime.UtcNow - lastFlush;
                if (batch.Count > 0 && (batch.Count >= maxBatchSize || timeSinceLastFlush.TotalMilliseconds >= maxWaitMs))
                {
                    await FlushAlertEventsAsync(batch);
                    batch.Clear();
                    lastFlush = DateTime.UtcNow;
                }

                if (batch.Count == 0)
                {
                    await _buffer.AlertEventReader.WaitToReadAsync(stoppingToken);
                }
                else
                {
                    await Task.Delay(100, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FederatedIngestionWorker: Error processing buffered alert events.");
                await Task.Delay(2000, stoppingToken);
            }
        }

        try
        {
            while (_buffer.AlertEventReader.TryRead(out var evt))
            {
                batch.Add(evt);
            }

            if (batch.Count > 0)
            {
                _logger.LogInformation("FederatedIngestionWorker: Flushing final {Count} alert events during shutdown...", batch.Count);
                await FlushAlertEventsAsync(batch);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FederatedIngestionWorker: Error flushing remaining alert events during shutdown.");
        }
    }

    private async Task FlushDevicesAsync(List<Device> batch)
    {
        if (batch == null || batch.Count == 0) return;

        // Normalize MAC addresses in-memory to guarantee grouping works perfectly
        foreach (var d in batch)
        {
            if (!string.IsNullOrWhiteSpace(d.MacAddress))
            {
                d.MacAddress = d.MacAddress.Replace("-", "").Replace(":", "").Replace(".", "").ToUpperInvariant();
            }
        }

        // De-duplicate in-memory: Keep only the chronologically LATEST update (last in the batch) 
        // for any unique { NodeId, MAC } or { NodeId, IP }
        var uniqueDevices = batch
            .GroupBy(d => new
            {
                NodeId = d.NodeId,
                MacAddress = d.MacAddress ?? string.Empty,
                IpAddress = d.MacAddress == null ? (d.IpAddress ?? string.Empty) : string.Empty
            })
            .Select(group => group.Last())
            .ToList();

        _logger.LogDebug("FederatedIngestionWorker: Telemetry buffer de-duplicated {Original} raw updates to {Unique} unique operations.", 
            batch.Count, 
            uniqueDevices.Count);

        using var scope = _scopeFactory.CreateScope();
        var deviceRepo = scope.ServiceProvider.GetRequiredService<IDeviceRepository>();

        int retries = 3;
        while (retries > 0)
        {
            try
            {
                deviceRepo.UpsertDevices(uniqueDevices);
                break; // Success!
            }
            catch (Microsoft.Data.Sqlite.SqliteException ex) when (SqliteConcurrency.IsLockError(ex) && retries > 1)
            {
                retries--;
                _logger.LogWarning("FederatedIngestionWorker: SQLite database locked during bulk telemetry flush. Retrying... ({Retries} left)", retries);
                await Task.Delay(250 * (3 - retries));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FederatedIngestionWorker: Fatal error executing bulk upsert for {Count} devices.", uniqueDevices.Count);
                break;
            }
        }
    }

    private async Task FlushLogsAsync(List<FederatedLog> batch)
    {
        if (batch == null || batch.Count == 0) return;

        using var scope = _scopeFactory.CreateScope();
        var contextFactory = scope.ServiceProvider.GetService<IDbContextFactory<HubDbContext>>();
        if (contextFactory == null)
        {
            _logger.LogWarning("FederatedIngestionWorker: HubDbContextFactory is missing. Cannot flush logs.");
            return;
        }

        int retries = 3;
        while (retries > 0)
        {
            try
            {
                using var context = await contextFactory.CreateDbContextAsync();
                context.FederatedLogs.AddRange(batch);
                await context.SaveChangesAsync();
                _logger.LogDebug("FederatedIngestionWorker: Successfully batch inserted {Count} diagnostic logs.", batch.Count);
                break; // Success!
            }
            catch (Microsoft.Data.Sqlite.SqliteException ex) when (SqliteConcurrency.IsLockError(ex) && retries > 1)
            {
                retries--;
                _logger.LogWarning("FederatedIngestionWorker: SQLite database locked during bulk logs flush. Retrying... ({Retries} left)", retries);
                await Task.Delay(250 * (3 - retries));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FederatedIngestionWorker: Fatal error executing bulk insert for {Count} logs.", batch.Count);
                break;
            }
        }
    }

    private async Task ProcessAuditEventsAsync(CancellationToken stoppingToken)
    {
        var batch = new List<AuditEvent>();
        var lastFlush = DateTime.UtcNow;
        const int maxBatchSize = 100;
        const int maxWaitMs = 1000;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_buffer.IsPaused)
                {
                    await Task.Delay(200, stoppingToken);
                    continue;
                }

                while (batch.Count < maxBatchSize && _buffer.AuditEventReader.TryRead(out var evt))
                {
                    batch.Add(evt);
                }

                var timeSinceLastFlush = DateTime.UtcNow - lastFlush;
                if (batch.Count > 0 && (batch.Count >= maxBatchSize || timeSinceLastFlush.TotalMilliseconds >= maxWaitMs))
                {
                    await FlushAuditEventsAsync(batch);
                    batch.Clear();
                    lastFlush = DateTime.UtcNow;
                }

                if (batch.Count == 0)
                {
                    await _buffer.AuditEventReader.WaitToReadAsync(stoppingToken);
                }
                else
                {
                    await Task.Delay(100, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FederatedIngestionWorker: Error processing buffered audit events.");
                await Task.Delay(2000, stoppingToken);
            }
        }

        try
        {
            while (_buffer.AuditEventReader.TryRead(out var evt))
            {
                batch.Add(evt);
            }

            if (batch.Count > 0)
            {
                _logger.LogInformation("FederatedIngestionWorker: Flushing final {Count} audit events during shutdown...", batch.Count);
                await FlushAuditEventsAsync(batch);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FederatedIngestionWorker: Error flushing remaining audit events during shutdown.");
        }
    }

    private async Task FlushSystemEventsAsync(List<SystemEvent> batch)
    {
        if (batch == null || batch.Count == 0) return;

        var uniqueEvents = batch
            .GroupBy(e => e.Id)
            .Select(g => g.Last())
            .ToList();

        foreach (var evt in uniqueEvents)
        {
            if (evt.Id == LiteDB.ObjectId.Empty)
                evt.Id = LiteDB.ObjectId.NewObjectId();
        }

        using var scope = _scopeFactory.CreateScope();
        var contextFactory = scope.ServiceProvider.GetService<IDbContextFactory<HubDbContext>>();
        if (contextFactory == null)
        {
            _logger.LogWarning("FederatedIngestionWorker: HubDbContextFactory is missing. Cannot flush system events.");
            return;
        }

        int retries = 3;
        while (retries > 0)
        {
            try
            {
                using var context = await contextFactory.CreateDbContextAsync();
                foreach (var evt in uniqueEvents)
                {
                    var existing = await context.SystemEvents.FindAsync(evt.Id);
                    if (existing != null)
                    {
                        context.Entry(existing).CurrentValues.SetValues(evt);
                    }
                    else
                    {
                        await context.SystemEvents.AddAsync(evt);
                    }
                }
                await context.SaveChangesAsync();
                _logger.LogDebug("FederatedIngestionWorker: Successfully batch inserted/updated {Count} system events.", uniqueEvents.Count);
                break; // Success!
            }
            catch (Microsoft.Data.Sqlite.SqliteException ex) when (SqliteConcurrency.IsLockError(ex) && retries > 1)
            {
                retries--;
                _logger.LogWarning("FederatedIngestionWorker: SQLite database locked during bulk system events flush. Retrying... ({Retries} left)", retries);
                await Task.Delay(250 * (3 - retries));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FederatedIngestionWorker: Fatal error executing bulk flush for {Count} system events.", uniqueEvents.Count);
                break;
            }
        }
    }

    private async Task FlushAuditEventsAsync(List<AuditEvent> batch)
    {
        if (batch == null || batch.Count == 0) return;

        var uniqueEvents = batch
            .GroupBy(e => e.Id)
            .Select(g => g.Last())
            .ToList();

        using var scope = _scopeFactory.CreateScope();
        var contextFactory = scope.ServiceProvider.GetService<IDbContextFactory<HubDbContext>>();
        if (contextFactory == null)
        {
            _logger.LogWarning("FederatedIngestionWorker: HubDbContextFactory is missing. Cannot flush audit events.");
            return;
        }

        int retries = 3;
        while (retries > 0)
        {
            try
            {
                using var context = await contextFactory.CreateDbContextAsync();
                foreach (var evt in uniqueEvents)
                {
                    // Anonymous actors (failed login) may arrive with null required columns after SignalR/LiteDB.
                    AuditEventNormalizer.Normalize(evt);
                    var existing = await context.AuditEvents.FindAsync(evt.Id);
                    if (existing != null)
                    {
                        context.Entry(existing).CurrentValues.SetValues(evt);
                    }
                    else
                    {
                        await context.AuditEvents.AddAsync(evt);
                    }
                }
                await context.SaveChangesAsync();
                _logger.LogDebug("FederatedIngestionWorker: Successfully batch inserted/updated {Count} audit events.", uniqueEvents.Count);
                break;
            }
            catch (Microsoft.Data.Sqlite.SqliteException ex) when (SqliteConcurrency.IsLockError(ex) && retries > 1)
            {
                retries--;
                _logger.LogWarning("FederatedIngestionWorker: SQLite database locked during bulk audit events flush. Retrying... ({Retries} left)", retries);
                await Task.Delay(250 * (3 - retries));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FederatedIngestionWorker: Fatal error executing bulk flush for {Count} audit events.", uniqueEvents.Count);
                break;
            }
        }
    }

    private async Task FlushAlertEventsAsync(List<AlertEvent> batch)
    {
        if (batch == null || batch.Count == 0) return;

        var uniqueAlerts = batch
            .GroupBy(e => e.Id)
            .Select(g => g.Last())
            .ToList();

        using var scope = _scopeFactory.CreateScope();
        var contextFactory = scope.ServiceProvider.GetService<IDbContextFactory<HubDbContext>>();
        if (contextFactory == null)
        {
            _logger.LogWarning("FederatedIngestionWorker: HubDbContextFactory is missing. Cannot flush alert events.");
            return;
        }

        int retries = 3;
        while (retries > 0)
        {
            try
            {
                using var context = await contextFactory.CreateDbContextAsync();
                foreach (var alert in uniqueAlerts)
                {
                    var existing = await context.AlertEvents.FindAsync(alert.Id);
                    if (existing != null)
                    {
                        context.Entry(existing).CurrentValues.SetValues(alert);
                    }
                    else
                    {
                        await context.AlertEvents.AddAsync(alert);
                    }
                }
                await context.SaveChangesAsync();
                _logger.LogDebug("FederatedIngestionWorker: Successfully batch inserted/updated {Count} alert events.", uniqueAlerts.Count);
                break; // Success!
            }
            catch (Microsoft.Data.Sqlite.SqliteException ex) when (SqliteConcurrency.IsLockError(ex) && retries > 1)
            {
                retries--;
                _logger.LogWarning("FederatedIngestionWorker: SQLite database locked during bulk alert events flush. Retrying... ({Retries} left)", retries);
                await Task.Delay(250 * (3 - retries));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FederatedIngestionWorker: Fatal error executing bulk flush for {Count} alert events.", uniqueAlerts.Count);
                break;
            }
        }
    }

    private async Task ProcessNodeHeartbeatsAsync(CancellationToken stoppingToken)
    {
        var pending = new Dictionary<string, FederatedNodeHeartbeat>(StringComparer.OrdinalIgnoreCase);
        var lastFlush = DateTime.UtcNow;
        const int maxWaitMs = 500;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_buffer.IsPaused)
                {
                    await Task.Delay(200, stoppingToken);
                    continue;
                }

                while (_buffer.NodeHeartbeatReader.TryRead(out var heartbeat))
                {
                    pending[heartbeat.NodeId] = heartbeat;
                }

                var timeSinceLastFlush = DateTime.UtcNow - lastFlush;
                if (pending.Count > 0 && timeSinceLastFlush.TotalMilliseconds >= maxWaitMs)
                {
                    await FlushNodeHeartbeatsAsync(pending);
                    pending.Clear();
                    lastFlush = DateTime.UtcNow;
                }

                if (pending.Count == 0)
                {
                    await _buffer.NodeHeartbeatReader.WaitToReadAsync(stoppingToken);
                }
                else
                {
                    await Task.Delay(50, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FederatedIngestionWorker: Error processing node heartbeats.");
                await Task.Delay(1000, stoppingToken);
            }
        }

        if (pending.Count > 0)
        {
            try
            {
                await FlushNodeHeartbeatsAsync(pending);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FederatedIngestionWorker: Error flushing final node heartbeats during shutdown.");
            }
        }
    }

    private async Task FlushNodeHeartbeatsAsync(Dictionary<string, FederatedNodeHeartbeat> heartbeats)
    {
        if (heartbeats.Count == 0) return;

        using var scope = _scopeFactory.CreateScope();
        var nodeRepo = scope.ServiceProvider.GetRequiredService<IFederatedNodeRepository>();

        foreach (var heartbeat in heartbeats.Values)
        {
            int retries = 3;
            while (retries > 0)
            {
                try
                {
                    var node = nodeRepo.GetById(heartbeat.NodeId);
                    if (node == null) break;

                    var signalRConnected = FederationSignalRRegistry.IsNodeConnected(node.Id, node.ConnectionId);

                    if (signalRConnected)
                    {
                        if (heartbeat.LastSyncUtc > node.LastSyncUtc)
                            node.LastSyncUtc = heartbeat.LastSyncUtc;

                        node.LastSeenUtc = heartbeat.LastSeenUtc;
                        if (!FederatedNodeStatus.ShouldPreserveOnHeartbeat(node.Status))
                            node.Status = FederatedNodeStatus.Online;
                    }
                    else if (!FederatedNodeStatus.ShouldPreserveOnHeartbeat(node.Status))
                    {
                        // Stale buffered heartbeat after disconnect  -  do not resurrect offline nodes.
                        node.Status = FederatedNodeStatus.Offline;
                        node.ConnectionId = null;
                    }

                    nodeRepo.UpsertNode(node);
                    break;
                }
                catch (Microsoft.Data.Sqlite.SqliteException ex) when (SqliteConcurrency.IsLockError(ex) && retries > 1)
                {
                    retries--;
                    _logger.LogWarning(
                        "FederatedIngestionWorker: SQLite locked updating node heartbeat for {NodeId}. Retrying... ({Retries} left)",
                        heartbeat.NodeId,
                        retries);
                    await Task.Delay(250 * (3 - retries));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "FederatedIngestionWorker: Failed to update node heartbeat for {NodeId}.", heartbeat.NodeId);
                    break;
                }
            }
        }
    }
}
