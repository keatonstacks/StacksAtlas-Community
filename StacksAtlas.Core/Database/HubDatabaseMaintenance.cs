using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StacksAtlas.Core.Abstractions;
using StacksAtlas.Core.Data.Hub;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Services.Security;
using StacksAtlas.Core.Settings;

namespace StacksAtlas.Core.Database;

/// <summary>
/// Relational-grade implementation of database maintenance for Hub mode.
/// Supports SQLite, PostgreSQL, and SQL Server with dynamic size querying and defragmentation.
/// </summary>
public class HubDatabaseMaintenance(
    IDbContextFactory<HubDbContext> contextFactory,
    SystemSettingsStore settingsStore,
    SettingsEncryptor encryptor,
    IConfiguration configuration,
    string databasePath,
    CleanupSettingsStore cleanupStore,
    ILogger<HubDatabaseMaintenance> logger,
    IClock clock) : IDatabaseMaintenance
{
    private readonly CleanupOptions _options = cleanupStore.Current;

    private (string Provider, string ConnectionString) GetActiveConnectionDetails()
    {
        var dbSettings = settingsStore.Current.Database;
        var provider = dbSettings?.Provider ?? "Sqlite";
        var connectionString = dbSettings?.ConnectionString;

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            provider = configuration["StacksAtlas:Database:Provider"] ?? provider;
            connectionString = configuration["StacksAtlas:Database:ConnectionString"];
        }

        if (!string.IsNullOrWhiteSpace(connectionString) && encryptor.IsEncrypted(connectionString))
        {
            connectionString = encryptor.Unprotect(connectionString);
        }

        return (provider, connectionString ?? string.Empty);
    }

    public async Task<long> GetDatabaseSizeBytesAsync()
    {
        try
        {
            var (provider, connectionString) = GetActiveConnectionDetails();

            switch (provider.ToLowerInvariant())
            {
                case "postgresql":
                case "postgres":
                case "npgsql":
                    return await GetPostgresDatabaseSizeAsync();

                case "sqlserver":
                case "mssql":
                    return await GetSqlServerDatabaseSizeAsync();

                case "sqlite":
                default:
                    return GetSqliteDatabaseSize(connectionString);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to resolve Hub database size.");
            return 0L;
        }
    }

    private long GetSqliteDatabaseSize(string connectionString)
    {
        string? hubDbPath = null;

        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            try
            {
                var builder = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(connectionString);
                hubDbPath = builder.DataSource;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to parse SQLite connection string for file size.");
            }
        }

        if (string.IsNullOrWhiteSpace(hubDbPath))
        {
            hubDbPath = Path.Combine(Path.GetDirectoryName(databasePath)!, "StacksAtlas.Hub.db");
        }
        else if (!Path.IsPathRooted(hubDbPath))
        {
            hubDbPath = Path.Combine(Path.GetDirectoryName(databasePath)!, hubDbPath);
        }

        return File.Exists(hubDbPath) ? new FileInfo(hubDbPath).Length : 0L;
    }

    private async Task<long> GetPostgresDatabaseSizeAsync()
    {
        using var context = await contextFactory.CreateDbContextAsync();
        using var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_database_size(current_database());";
        var result = await command.ExecuteScalarAsync();
        return result != null ? Convert.ToInt64(result) : 0L;
    }

    private async Task<long> GetSqlServerDatabaseSizeAsync()
    {
        using var context = await contextFactory.CreateDbContextAsync();
        using var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT CAST(SUM(size) * 8 * 1024 AS bigint) FROM sys.master_files WHERE database_id = DB_ID();";
        var result = await command.ExecuteScalarAsync();
        return result != null ? Convert.ToInt64(result) : 0L;
    }

    public async Task<int> CleanupAsync(CancellationToken token = default)
    {
        if (token.IsCancellationRequested || !_options.Enabled)
        {
            return 0;
        }

        int changedCount = 0;
        using var context = await contextFactory.CreateDbContextAsync(token);

        // --- STAGE 1: AUTO-ARCHIVE STALE DEVICES ---
        var archiveCutoff = clock.UtcNow.AddDays(-_options.AutoArchiveDays);
        var toArchive = await context.Devices
            .Where(d => !d.IsDeleted && d.LastSeen < archiveCutoff)
            .ToListAsync(token);

        foreach (var device in toArchive)
        {
            if (token.IsCancellationRequested) break;
            device.IsDeleted = true;
            device.ArchivedUtc ??= clock.UtcNow;
            device.LastModifiedUtc = clock.UtcNow;
            context.Devices.Update(device);
            changedCount++;
            logger.LogInformation("Hub: Auto-archived stale device: {DeviceId} from site {NodeId}", device.Id, device.NodeId);
        }

        if (toArchive.Count > 0 && !token.IsCancellationRequested)
        {
            await context.SaveChangesAsync(token);
        }

        // --- STAGE 2: PERMANENT PURGE OF ARCHIVED DEVICES ---
        if (_options.PermanentDeletionDays > 0 && !token.IsCancellationRequested)
        {
            var purgeCutoff = clock.UtcNow.AddDays(-_options.PermanentDeletionDays);

            var legacyArchived = await context.Devices
                .Where(d => d.IsDeleted && d.ArchivedUtc == null)
                .ToListAsync(token);
            foreach (var legacy in legacyArchived)
            {
                legacy.ArchivedUtc = legacy.LastModifiedUtc > DateTime.MinValue
                    ? legacy.LastModifiedUtc
                    : legacy.LastSeen;
                context.Devices.Update(legacy);
            }

            if (legacyArchived.Count > 0)
                await context.SaveChangesAsync(token);

            var toPurge = await context.Devices
                .Where(d => d.IsDeleted && d.ArchivedUtc != null && d.ArchivedUtc < purgeCutoff)
                .ToListAsync(token);

            if (toPurge.Count > 0)
            {
                context.Devices.RemoveRange(toPurge);
                changedCount += toPurge.Count;
                await context.SaveChangesAsync(token);
                logger.LogInformation("Hub: Permanently purged {PurgeCount} archived devices older than {PurgeDate}", toPurge.Count, purgeCutoff);
            }
        }

        // --- STAGE 3: FLUSH/PRUNE HISTORIC LOGS ---
        if (!token.IsCancellationRequested)
        {
            // Prune streamed Node logs older than the archive window (or a sensible default like 30 days)
            var logCutoff = clock.UtcNow.AddDays(-Math.Max(_options.AutoArchiveDays, 14));
            var toDeleteLogs = await context.FederatedLogs
                .Where(l => l.Timestamp < logCutoff)
                .ToListAsync(token);

            if (toDeleteLogs.Count > 0)
            {
                context.FederatedLogs.RemoveRange(toDeleteLogs);
                await context.SaveChangesAsync(token);
                logger.LogInformation("Hub: Pruned {LogCount} historic Node log streams older than {LogDate}", toDeleteLogs.Count, logCutoff);
            }
        }

        return changedCount;
    }

    public async Task CompactAsync(CancellationToken token = default)
    {
        if (token.IsCancellationRequested) return;

        var (provider, _) = GetActiveConnectionDetails();
        if (!provider.Equals("sqlite", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("Hub: Compaction/Vacuum is skipped for RDBMS database provider: {Provider}", provider);
            return;
        }

        logger.LogInformation("Hub: Executing SQLite database compaction (VACUUM)...");

        try
        {
            using var context = await contextFactory.CreateDbContextAsync(token);
            // Run SQLite VACUUM command to optimize storage and reclaim free space
            await context.Database.ExecuteSqlRawAsync("VACUUM;", token);
            logger.LogInformation("Hub: SQLite database compaction completed successfully.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Hub: Compaction failed on SQLite relational store.");
        }
    }
}
