using LiteDB;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StacksAtlas.Core.Abstractions;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Services.Security;
using StacksAtlas.Core.Settings;

namespace StacksAtlas.Core.Database;

/// <summary>
/// Industrial-grade implementation of database maintenance for LiteDB.
/// Implements aggressive memory management and deterministic temporal logic.
/// </summary>
public class LiteDbDatabaseMaintenance(
    LiteDatabase db,
    string dbPath,
    CleanupSettingsStore settingsStore,
    ILogger<LiteDbDatabaseMaintenance> logger,
    DatabaseSecurityService securityService,
    IClock clock) : IDatabaseMaintenance
{
    private readonly CleanupOptions _options = settingsStore.Current;

    public Task<long> GetDatabaseSizeBytesAsync()
    {
        var fileInfo = new FileInfo(dbPath);
        return Task.FromResult(fileInfo.Exists ? fileInfo.Length : 0L);
    }

    public async Task<int> CleanupAsync(CancellationToken token = default)
    {
        if (token.IsCancellationRequested || !_options.Enabled)
        {
            return 0;
        }

        return await Task.Run(() =>
        {
            var devices = db.GetCollection<Device>("devices");
            int changedCount = 0;

            // Architectural Optimization: Use database-side queries instead of Materializing All to List
            // This prevents OOM spikes on large network registries.

            // --- STAGE 1: AUTO-ARCHIVE ---
            var archiveCutoff = clock.UtcNow.AddDays(-_options.AutoArchiveDays);
            
            // LiteDB doesn't support complex joins in UpdateMany, so we find IDs first
            // but we only project the IDs to keep memory footprint minimal.
            var toArchiveIds = devices.Query()
                .Where(d => !d.IsDeleted && d.LastSeen < archiveCutoff)
                .Select(d => d.Id)
                .ToList();

            foreach (var id in toArchiveIds)
            {
                if (token.IsCancellationRequested) break;
                
                var device = devices.FindById(id);
                if (device != null)
                {
                    device.IsDeleted = true;
                    device.ArchivedUtc ??= clock.UtcNow;
                    devices.Update(device);
                    changedCount++;
                    logger.LogInformation("Auto-archived stale device: {DeviceId}", id);
                }
            }

            // --- STAGE 2: PERMANENT PURGE ---
            if (_options.PermanentDeletionDays > 0)
            {
                var purgeCutoff = clock.UtcNow.AddDays(-_options.PermanentDeletionDays);

                // Legacy rows archived before ArchivedUtc existed: stamp from LastModifiedUtc/LastSeen once.
                var legacyArchived = devices.Query()
                    .Where(d => d.IsDeleted && d.ArchivedUtc == null)
                    .ToList();
                foreach (var legacy in legacyArchived)
                {
                    legacy.ArchivedUtc = legacy.LastModifiedUtc > DateTime.MinValue
                        ? legacy.LastModifiedUtc
                        : legacy.LastSeen;
                    devices.Update(legacy);
                }

                // Purge by archive time, not LastSeen (avoids instant wipe of freshly archived stale hosts).
                changedCount += devices.DeleteMany(d =>
                    d.IsDeleted
                    && d.ArchivedUtc != null
                    && d.ArchivedUtc < purgeCutoff);
                
                logger.LogInformation("Permanently purged archived devices older than {PurgeDate}", purgeCutoff);
            }

            return changedCount;
        }, token);
    }

    public async Task CompactAsync(CancellationToken token = default)
    {
        if (token.IsCancellationRequested)
            return;

        logger.LogInformation("LiteDB compaction starting...");

        // Architectural Hardening: Use injected security service instead of manual instantiation
        var dbPassword = securityService.GetDatabasePassword();

        await Task.Run(() =>
        {
            // IMPORTANT: Rebuild requires an exclusive lock. 
            // We use a separate instance to perform the rebuild operation.
            using var dbInstance = new LiteDatabase(new ConnectionString(dbPath) { Password = dbPassword });
            var options = !string.IsNullOrEmpty(dbPassword) 
                ? new LiteDB.Engine.RebuildOptions { Password = dbPassword } 
                : null;
            dbInstance.Rebuild(options);
        }, token);

        logger.LogInformation("LiteDB compaction completed.");
    }
}
