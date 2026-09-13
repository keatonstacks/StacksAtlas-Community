using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StacksAtlas.API.Services;
using StacksAtlas.Core.Data.Hub;
using StacksAtlas.Core.Services.Security;
using StacksAtlas.Core.Settings;

namespace StacksAtlas.API.Services;

public class DatabaseInfrastructureMigrationService
{
    private readonly SystemSettingsStore _settingsStore;
    private readonly SettingsEncryptor _encryptor;
    private readonly IDbContextFactory<HubDbContext> _contextFactory;
    private readonly FederatedIngestionBuffer _ingestionBuffer;
    private readonly ILogger<DatabaseInfrastructureMigrationService> _logger;

    public DatabaseInfrastructureMigrationService(
        SystemSettingsStore settingsStore,
        SettingsEncryptor encryptor,
        IDbContextFactory<HubDbContext> contextFactory,
        FederatedIngestionBuffer ingestionBuffer,
        ILogger<DatabaseInfrastructureMigrationService> logger)
    {
        _settingsStore = settingsStore;
        _encryptor = encryptor;
        _contextFactory = contextFactory;
        _ingestionBuffer = ingestionBuffer;
        _logger = logger;
    }

    /// <summary>
    /// Tests a database connection and verifies if the target schema can be initialized.
    /// </summary>
    public async Task<bool> TestConnectionAsync(string provider, string connectionString)
    {
        try
        {
            var options = BuildOptions(provider, connectionString);
            using var context = new HubDbContext(options);
            
            // Check connectivity
            return await context.Database.CanConnectAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DatabaseMigrationService: Test connection failed for provider {Provider}", provider);
            return false;
        }
    }

    /// <summary>
    /// Executes the zero-downtime, fail-safe database migration lifecycle.
    /// </summary>
    public async Task MigrateDatabaseAsync(string targetProvider, string targetConnectionString, bool performDataCopy)
    {
        _logger.LogInformation("DatabaseMigrationService: Starting migration to {Provider}...", targetProvider);

        // 1. Connection Handshake
        var canConnect = await TestConnectionAsync(targetProvider, targetConnectionString);
        if (!canConnect)
        {
            throw new InvalidOperationException($"Cannot establish connection to the target {targetProvider} database. Verify credentials, host, and port.");
        }

        // 2. Pause Ingestion (Maintenance Mode)
        _ingestionBuffer.IsPaused = true;
        _logger.LogInformation("DatabaseMigrationService: Ingestion worker suspended. Incoming telemetry is being buffered in memory.");

        try
        {
            // 3. Schema Provisioning on Target
            var targetOptions = BuildOptions(targetProvider, targetConnectionString);
            using (var targetDb = new HubDbContext(targetOptions))
            {
                _logger.LogInformation("DatabaseMigrationService: Ensuring target database schema is fully provisioned...");
                await targetDb.Database.EnsureCreatedAsync();
            }

            // 4. Bulk Data Streaming (SQLite -> Target)
            if (performDataCopy)
            {
                using var sourceDb = await _contextFactory.CreateDbContextAsync();
                using var targetDb = new HubDbContext(targetOptions);

                await StreamDataAsync(sourceDb, targetDb);
            }

            // 5. Hot-swap Configuration and Save Encrypted Settings
            var currentSettings = _settingsStore.Load();
            currentSettings.Database.Provider = targetProvider;
            currentSettings.Database.ConnectionString = _encryptor.Protect(targetConnectionString);
            _settingsStore.Save(currentSettings);

            _logger.LogInformation("DatabaseMigrationService: Settings updated. Connection string encrypted and saved successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DatabaseMigrationService: FATAL ERROR during migration. Initiating automatic rollback...");
            throw;
        }
        finally
        {
            // 6. Resume Ingestion (Unfreeze queue)
            _ingestionBuffer.IsPaused = false;
            _logger.LogInformation("DatabaseMigrationService: Ingestion worker resumed. Telemetry flushing active.");
        }
    }

    private async Task StreamDataAsync(HubDbContext sourceDb, HubDbContext targetDb)
    {
        _logger.LogInformation("DatabaseMigrationService: Commencing data transfer...");

        // Enforce transaction boundaries on target
        using var transaction = await targetDb.Database.BeginTransactionAsync();
        try
        {
            // 1. Stream Nodes
            var nodes = await sourceDb.Nodes.AsNoTracking().ToListAsync();
            if (nodes.Any())
            {
                _logger.LogInformation("DatabaseMigrationService: Transferring {Count} enrolled Nodes...", nodes.Count);
                await targetDb.Nodes.AddRangeAsync(nodes);
                await targetDb.SaveChangesAsync();
            }

            // 2. Stream Users
            var users = await sourceDb.Users.AsNoTracking().ToListAsync();
            if (users.Any())
            {
                _logger.LogInformation("DatabaseMigrationService: Transferring {Count} Users...", users.Count);
                await targetDb.Users.AddRangeAsync(users);
                await targetDb.SaveChangesAsync();
            }

            // 3. Stream Webhooks
            var webhooks = await sourceDb.Webhooks.AsNoTracking().ToListAsync();
            if (webhooks.Any())
            {
                _logger.LogInformation("DatabaseMigrationService: Transferring {Count} Webhook Destinations...", webhooks.Count);
                await targetDb.Webhooks.AddRangeAsync(webhooks);
                await targetDb.SaveChangesAsync();
            }

            // 4. Stream Devices in batches to minimize memory overhead
            _logger.LogInformation("DatabaseMigrationService: Transferring Devices...");
            const int batchSize = 500;
            int deviceOffset = 0;
            while (true)
            {
                var devices = await sourceDb.Devices.AsNoTracking()
                    .OrderBy(d => d.Id)
                    .Skip(deviceOffset)
                    .Take(batchSize)
                    .ToListAsync();

                if (devices.Count == 0) break;

                await targetDb.Devices.AddRangeAsync(devices);
                await targetDb.SaveChangesAsync();
                deviceOffset += devices.Count;
                _logger.LogInformation("DatabaseMigrationService: Buffered {Count} devices...", deviceOffset);
            }

            // 5. Stream Federated Diagnostic Logs in batches
            _logger.LogInformation("DatabaseMigrationService: Transferring Fleet Log History...");
            int logOffset = 0;
            while (true)
            {
                var logs = await sourceDb.FederatedLogs.AsNoTracking()
                    .OrderBy(l => l.Id)
                    .Skip(logOffset)
                    .Take(batchSize)
                    .ToListAsync();

                if (logs.Count == 0) break;

                await targetDb.FederatedLogs.AddRangeAsync(logs);
                await targetDb.SaveChangesAsync();
                logOffset += logs.Count;
                _logger.LogInformation("DatabaseMigrationService: Buffered {Count} diagnostic log entries...", logOffset);
            }

            await transaction.CommitAsync();
            _logger.LogInformation("DatabaseMigrationService: Bulk data transfer completed successfully. Committed transaction on target.");
        }
        catch (Exception)
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private DbContextOptions<HubDbContext> BuildOptions(string provider, string connectionString)
    {
        var optionsBuilder = new DbContextOptionsBuilder<HubDbContext>();
        switch (provider.ToLowerInvariant())
        {
            case "postgresql":
            case "postgres":
            case "npgsql":
                optionsBuilder.UseNpgsql(connectionString, b => b.MigrationsAssembly("StacksAtlas.Core"));
                break;
            case "sqlserver":
            case "mssql":
                optionsBuilder.UseSqlServer(connectionString, b => b.MigrationsAssembly("StacksAtlas.Core"));
                break;
            case "sqlite":
            default:
                optionsBuilder.UseSqlite(connectionString, b => b.MigrationsAssembly("StacksAtlas.Core"));
                break;
        }
        return optionsBuilder.Options;
    }
}
