using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection; // For IServiceScopeFactory
using LiteDB;
using StacksAtlas.Core.Services.Security;
using System.IO; // Added
using System.Threading.Tasks; // Added
using System.Threading; // Added
using StacksAtlas.Core.Data;
using StacksAtlas.Core.Services.Devices;
using System.Collections.Generic;
using StacksAtlas.Core.Data.Hub;
using Microsoft.EntityFrameworkCore;
using StacksAtlas.Core.State;
using StacksAtlas.Core.Services.Auth;
using StacksAtlas.Core.Settings;
using StacksAtlas.API.Services;

namespace StacksAtlas.API.Workers;

public class DatabaseStartupService : BackgroundService
{
    private readonly ILogger<DatabaseStartupService> _logger;
    private readonly DatabaseSecurityService _dbSecurity;
    private readonly StacksAtlas.Core.State.SystemStateProvider _stateProvider;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly string _databasePath;

    public DatabaseStartupService(
        ILogger<DatabaseStartupService> logger,
        DatabaseSecurityService dbSecurity,
        StacksAtlas.Core.State.SystemStateProvider stateProvider,
        IServiceScopeFactory scopeFactory,
        string databasePath)
    {
        _logger = logger;
        _dbSecurity = dbSecurity;
        _stateProvider = stateProvider;
        _scopeFactory = scopeFactory;
        _databasePath = databasePath;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Database startup checks initiated in background...");

        // Note: Security checks and SQLite WAL initialization are now handled sequentially 
        // by the DatabaseBootstrapper to prevent concurrent file-access conflicts.
        
        // --- HUB DB SCHEMA HOTFIX: Ensure Client/Building/Room columns and tables exist ---
        if (ExecutionState.IsHubBrainEnabled)
        {
            try
            {
                _logger.LogInformation("Verifying Hub database schema integrity...");
                using (var scope = _scopeFactory.CreateScope())
                {
                    var context = scope.ServiceProvider.GetRequiredService<HubDbContext>();
                    var providerName = context.Database.ProviderName;
                    _logger.LogInformation("Hub database provider detected: {Provider}", providerName);

                    if (providerName != "Microsoft.EntityFrameworkCore.Sqlite")
                    {
                        _logger.LogInformation("DatabaseStartupService: Applying EF Core schema provisioning for provider {Provider}...", providerName);
                        await context.Database.EnsureCreatedAsync(stoppingToken);
                    }
                    else
                    {
                        if (!await TableExistsAsync(context, "Devices", stoppingToken))
                        {
                            _logger.LogInformation(
                                "Hub SQLite database is empty (e.g. post Fresh Start fleet wipe)  -  provisioning EF Core schema...");
                            await context.Database.EnsureCreatedAsync(stoppingToken);
                        }

                        var connection = context.Database.GetDbConnection();
                        await connection.OpenAsync(stoppingToken);

                        // Check and fix Devices table
                        await EnsureColumnExists(connection, "Devices", "Client", "TEXT");
                        await EnsureColumnExists(connection, "Devices", "Building", "TEXT");
                        await EnsureColumnExists(connection, "Devices", "Room", "TEXT");
                        await EnsureColumnExists(connection, "Devices", "FirstDiscoveredUtc", "TEXT");
                        await EnsureColumnExists(connection, "Devices", "DiscoveryScopeId", "TEXT");
                        await EnsureColumnExists(connection, "Devices", "DiscoveryInterfaceId", "TEXT");
                        await EnsureColumnExists(connection, "Devices", "DiscoveryInterfaceName", "TEXT");
                        await EnsureColumnExists(connection, "Devices", "IsDiscoveryProvenanceManuallySet", "INTEGER DEFAULT 0");
                        await EnsureColumnExists(connection, "Devices", "IsTypeManuallySet", "INTEGER DEFAULT 0");
                        await EnsureColumnExists(connection, "Devices", "IsHostnameManuallySet", "INTEGER DEFAULT 0");
                        await EnsureColumnExists(connection, "Devices", "SerialNumber", "TEXT");
                        await EnsureColumnExists(connection, "Devices", "AssetTag", "TEXT");
                        await EnsureColumnExists(connection, "Devices", "FirmwareVersion", "TEXT");
                        await EnsureColumnExists(connection, "Devices", "WarrantyExpiresUtc", "TEXT");
                        await EnsureColumnExists(connection, "Devices", "IsSerialNumberManuallySet", "INTEGER DEFAULT 0");
                        await EnsureColumnExists(connection, "Devices", "IsFirmwareVersionManuallySet", "INTEGER DEFAULT 0");
                        await EnsureColumnExists(connection, "Devices", "IsAssetTagManuallySet", "INTEGER DEFAULT 0");
                        await EnsureColumnExists(connection, "Devices", "IsWarrantyExpiresManuallySet", "INTEGER DEFAULT 0");
                        await EnsureColumnExists(connection, "Devices", "AssetMetadataSource", "INTEGER DEFAULT 0");
                        await EnsureColumnExists(connection, "Devices", "DiscoveryVlanTag", "TEXT");
                        await EnsureColumnExists(connection, "Devices", "AttachmentKind", "TEXT DEFAULT 'Unknown'");
                        await EnsureColumnExists(connection, "Devices", "AttachmentPort", "TEXT");
                        await EnsureColumnExists(connection, "Devices", "AttachmentParentDeviceId", "TEXT");
                        await EnsureColumnExists(connection, "Devices", "AttachmentParentMac", "TEXT");
                        await EnsureColumnExists(connection, "Devices", "AttachmentParentIp", "TEXT");
                        await EnsureColumnExists(connection, "Devices", "IsAttachmentManuallySet", "INTEGER DEFAULT 0");
                        await EnsureColumnExists(connection, "Devices", "ArchivedUtc", "TEXT");
                        await EnsureColumnExists(connection, "Devices", "IsPermanentlyRemoved", "INTEGER DEFAULT 0");
                        await EnsureColumnExists(connection, "Devices", "RemovedUtc", "TEXT");
                        await EnsureColumnExists(connection, "Devices", "RemovedBy", "TEXT");
                        await EnsureColumnExists(connection, "Devices", "RemovedReason", "TEXT");
                        await EnsureColumnExists(connection, "Devices", "LastRediscoveryAttemptUtc", "TEXT");
                        await EnsureColumnExists(connection, "Devices", "LastRediscoveryIp", "TEXT");
                        await EnsureColumnExists(connection, "Devices", "RediscoveryHitCount", "INTEGER DEFAULT 0");
                        await EnsureDeviceScopeIndexAsync(connection, stoppingToken);

                        var createSuppressionsSql = @"
                            CREATE TABLE IF NOT EXISTS DeviceSuppressions (
                                Id TEXT PRIMARY KEY,
                                NodeId TEXT NOT NULL,
                                NormalizedMac TEXT NOT NULL,
                                DeviceId TEXT NOT NULL,
                                SuppressedUtc TEXT NOT NULL,
                                RemovedBy TEXT NULL
                            );
                            CREATE UNIQUE INDEX IF NOT EXISTS IX_DeviceSuppressions_NodeId_NormalizedMac
                                ON DeviceSuppressions(NodeId, NormalizedMac);
                        ";
                        await EnsureTableExists(connection, "DeviceSuppressions", createSuppressionsSql);

                        // Check and fix Nodes table
                        await EnsureColumnExists(connection, "Nodes", "Client", "TEXT");
                        await EnsureColumnExists(connection, "Nodes", "Building", "TEXT");
                        await EnsureColumnExists(connection, "Nodes", "Room", "TEXT");
                        await EnsureColumnExists(connection, "Nodes", "SyncUsers", "INTEGER DEFAULT 1");
                        await EnsureColumnExists(connection, "Nodes", "SyncUserRegistry", "INTEGER DEFAULT 0");
                        await EnsureColumnExists(connection, "Nodes", "SyncSsoSettings", "INTEGER DEFAULT 0");
                        await EnsureColumnExists(connection, "Nodes", "SyncAlertSettings", "INTEGER DEFAULT 0");
                        await EnsureColumnExists(connection, "Nodes", "SyncSiemSettings", "INTEGER DEFAULT 0");
                        await EnsureColumnExists(connection, "Nodes", "OverrideAlertSettings", "INTEGER DEFAULT 0");
                        await EnsureColumnExists(connection, "Nodes", "OverrideSiemSettings", "INTEGER DEFAULT 0");
                        await EnsureColumnExists(connection, "Nodes", "ScanSettingsJson", "TEXT");
                        await EnsureColumnExists(connection, "Nodes", "IsIdentityImported", "INTEGER DEFAULT 0");
                        await EnsureColumnExists(connection, "Nodes", "DatabaseSize", "INTEGER DEFAULT 0");
                        await EnsureColumnExists(connection, "Nodes", "CertificateSerialNumber", "TEXT");
                        await EnsureColumnExists(connection, "Nodes", "IsRevoked", "INTEGER DEFAULT 0");
                        await EnsureColumnExists(connection, "Nodes", "HardwareId", "TEXT");
                        await EnsureColumnExists(connection, "Nodes", "DelegateAlertDispatch", "INTEGER DEFAULT 0");
                        await EnsureColumnExists(connection, "Nodes", "HttpPort", "INTEGER DEFAULT 5000");
                        await EnsureColumnExists(connection, "Nodes", "HttpsPort", "INTEGER DEFAULT 5001");
                        await EnsureColumnExists(connection, "Nodes", "IsDebugLoggingEnabled", "INTEGER DEFAULT 0");
                        await EnsureColumnExists(connection, "Nodes", "IsPortable", "INTEGER DEFAULT 0");

                        // Migrate legacy nodes where SyncUsers is active
                        using (var command = connection.CreateCommand())
                        {
                            command.CommandText = "UPDATE Nodes SET SyncUserRegistry = 1, SyncSsoSettings = 1, SyncUsers = 0 WHERE SyncUsers = 1 AND SyncUserRegistry = 0 AND SyncSsoSettings = 0;";
                            var migratedRows = await command.ExecuteNonQueryAsync(stoppingToken);
                            if (migratedRows > 0)
                            {
                                _logger.LogInformation("DatabaseStartupService: Migrated {Count} legacy nodes to split-governance registry/SSO schemas.", migratedRows);
                            }
                        }

                        // Ensure FederatedLogs table exists
                        var createLogsSql = @"
                            CREATE TABLE IF NOT EXISTS FederatedLogs (
                                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                                NodeId TEXT NOT NULL,
                                Timestamp TEXT NOT NULL,
                                LogLevel TEXT NOT NULL,
                                Message TEXT NOT NULL,
                                Exception TEXT NULL
                            );
                            CREATE INDEX IF NOT EXISTS IX_FederatedLogs_NodeId_Timestamp ON FederatedLogs(NodeId, Timestamp);
                        ";
                        await EnsureTableExists(connection, "FederatedLogs", createLogsSql);

                        // Ensure SystemEvents table exists
                        var createSystemEventsSql = @"
                            CREATE TABLE IF NOT EXISTS SystemEvents (
                                Id TEXT PRIMARY KEY,
                                Timestamp TEXT NOT NULL,
                                Type TEXT NOT NULL,
                                Message TEXT NOT NULL,
                                DeviceIp TEXT NULL,
                                Subnet TEXT NULL,
                                Severity TEXT NOT NULL,
                                NodeId TEXT NULL,
                                NodeName TEXT NULL,
                                Client TEXT NULL,
                                Building TEXT NULL,
                                Room TEXT NULL
                            );
                            CREATE INDEX IF NOT EXISTS IX_SystemEvents_NodeId_Timestamp ON SystemEvents(NodeId, Timestamp);
                        ";
                        await EnsureTableExists(connection, "SystemEvents", createSystemEventsSql);
                        await EnsureColumnExists(connection, "SystemEvents", "NodeName", "TEXT");
                        await EnsureColumnExists(connection, "SystemEvents", "Client", "TEXT");
                        await EnsureColumnExists(connection, "SystemEvents", "Building", "TEXT");
                        await EnsureColumnExists(connection, "SystemEvents", "Room", "TEXT");

                        // Ensure AlertEvents table exists
                        var createAlertEventsSql = @"
                            CREATE TABLE IF NOT EXISTS AlertEvents (
                                Id TEXT PRIMARY KEY,
                                TriggeredAt TEXT NOT NULL,
                                DeviceId TEXT NOT NULL,
                                DeviceName TEXT NOT NULL,
                                DeviceIp TEXT NOT NULL,
                                AlertType INTEGER NOT NULL,
                                SentToEmails TEXT NOT NULL,
                                SentToWebhooks TEXT NOT NULL,
                                Success INTEGER NOT NULL,
                                ErrorMessage TEXT NULL,
                                NodeId TEXT NULL,
                                NodeName TEXT NULL,
                                Client TEXT NULL,
                                Building TEXT NULL,
                                Room TEXT NULL
                            );
                            CREATE INDEX IF NOT EXISTS IX_AlertEvents_NodeId_TriggeredAt ON AlertEvents(NodeId, TriggeredAt);
                        ";
                        await EnsureTableExists(connection, "AlertEvents", createAlertEventsSql);
                        await EnsureColumnExists(connection, "AlertEvents", "NodeName", "TEXT");
                        await EnsureColumnExists(connection, "AlertEvents", "Client", "TEXT");
                        await EnsureColumnExists(connection, "AlertEvents", "Building", "TEXT");
                        await EnsureColumnExists(connection, "AlertEvents", "Room", "TEXT");

                        var createAuditEventsSql = @"
                            CREATE TABLE IF NOT EXISTS AuditEvents (
                                Id TEXT PRIMARY KEY,
                                TimestampUtc TEXT NOT NULL,
                                Action TEXT NOT NULL,
                                ActorUserId TEXT NOT NULL,
                                ActorUsername TEXT NOT NULL,
                                ActorRole TEXT NOT NULL,
                                ResourceType TEXT NOT NULL,
                                ResourceId TEXT NULL,
                                Outcome TEXT NOT NULL,
                                ClientIp TEXT NULL,
                                Detail TEXT NULL,
                                NodeId TEXT NULL,
                                NodeName TEXT NULL
                            );
                            CREATE INDEX IF NOT EXISTS IX_AuditEvents_TimestampUtc ON AuditEvents(TimestampUtc);
                            CREATE INDEX IF NOT EXISTS IX_AuditEvents_Action ON AuditEvents(Action);
                            CREATE INDEX IF NOT EXISTS IX_AuditEvents_NodeId ON AuditEvents(NodeId);
                        ";
                        await EnsureTableExists(connection, "AuditEvents", createAuditEventsSql);
                    }
                }
                _logger.LogInformation("Hub schema integrity verified.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to verify or update Hub schema.");
            }
        }

        // --- HUB ORPHANED TELEMETRY CLEANUP: Purge data from deleted/non-existent nodes ---
        if (ExecutionState.IsHubBrainEnabled)
        {
            try
            {
                _logger.LogInformation("Cleaning up orphaned federated devices and logs...");
                using (var scope = _scopeFactory.CreateScope())
                {
                    var context = scope.ServiceProvider.GetRequiredService<HubDbContext>();
                    var activeNodeIds = await context.Nodes.Select(n => n.Id).ToListAsync(stoppingToken);

                    var orphanedDevices = await context.Devices
                        .Where(d => !string.IsNullOrEmpty(d.NodeId) && !activeNodeIds.Contains(d.NodeId))
                        .ToListAsync(stoppingToken);

                    if (orphanedDevices.Count > 0)
                    {
                        _logger.LogInformation("DatabaseStartupService: Found {Count} orphaned devices from deleted/non-existent nodes. Cleaning them up...", orphanedDevices.Count);
                        context.Devices.RemoveRange(orphanedDevices);
                    }

                    var orphanedLogs = await context.FederatedLogs
                        .Where(l => !string.IsNullOrEmpty(l.NodeId) && !activeNodeIds.Contains(l.NodeId))
                        .ToListAsync(stoppingToken);

                    if (orphanedLogs.Count > 0)
                    {
                        _logger.LogInformation("DatabaseStartupService: Found {Count} orphaned log entries. Cleaning them up...", orphanedLogs.Count);
                        context.FederatedLogs.RemoveRange(orphanedLogs);
                    }

                    if (orphanedDevices.Count > 0 || orphanedLogs.Count > 0)
                    {
                        await context.SaveChangesAsync(stoppingToken);
                        _logger.LogInformation("Orphaned telemetry cleanup complete.");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to clean up orphaned devices/logs on Hub startup.");
            }
        }

        // --- METRICS REPAIR: Ensure all devices have valid stats (fix 0% regressions) ---

        try 
        {
             _logger.LogInformation("Starting global metrics recalculation...");
             using (var scope = _scopeFactory.CreateScope())
             {
                 var repo = scope.ServiceProvider.GetRequiredService<IDeviceRepository>();
                 var allDevices = repo.GetAll().ToList();
                 var updates = new List<StacksAtlas.Core.Models.Device>();
                 
                 foreach(var d in allDevices)
                 {
                     // LEGACY MIGRATION: Fix zeroed metrics from version drift
                     if (d.TotalSweepsSeen == 0 && d.ScanCount > 0) 
                         d.TotalSweepsSeen = d.ScanCount;
                     
                     // If device is online but has no online history (legacy), assume good history to fix 0% display
                     if (d.TotalSweepsOnline == 0 && d.TotalSweepsSeen > 0 && d.Status?.ToLower() == "online")
                     {
                         d.TotalSweepsOnline = d.TotalSweepsSeen;
                     }

                     DeviceStabilityCalculator.UpdateDeviceStability(d);
                     updates.Add(d);
                 }
                 
                 if (updates.Any())
                 {
                     repo.UpsertDevices(updates);
                     _logger.LogInformation("Performed global metrics recalculation for {Count} devices.", updates.Count);
                 }
             }
        }
        catch (Exception ex)
        {
             _logger.LogError(ex, "Failed to recalculate metrics on startup.");
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var settingsStore = scope.ServiceProvider.GetRequiredService<SystemSettingsStore>();
            var authService = scope.ServiceProvider.GetRequiredService<IAuthService>();
            PortableOnboardingBootstrap.ApplyIfNeeded(settingsStore, authService, _logger);
            OnboardingLegacyMigration.ApplyIfNeeded(settingsStore, authService, _logger);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Portable / legacy onboarding bootstrap skipped due to error.");
        }

        _stateProvider.IsDatabaseReady = true;

        _logger.LogInformation("Database startup checks completed. System Ready.");
    }
    private static async Task<bool> TableExistsAsync(HubDbContext context, string tableName, CancellationToken token)
    {
        var connection = context.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync(token);

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name=$name;";
            var param = command.CreateParameter();
            param.ParameterName = "$name";
            param.Value = tableName;
            command.Parameters.Add(param);
            var result = await command.ExecuteScalarAsync(token);
            return result != null && result != DBNull.Value;
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }
    }

    private async Task EnsureTableExists(System.Data.Common.DbConnection connection, string tableName, string createTableSql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT name FROM sqlite_master WHERE type='table' AND name='{tableName}';";
        var result = await command.ExecuteScalarAsync();
        
        if (result == null || result == DBNull.Value)
        {
            _logger.LogInformation("Creating missing table '{Table}'...", tableName);
            using var createCommand = connection.CreateCommand();
            createCommand.CommandText = createTableSql;
            await createCommand.ExecuteNonQueryAsync();
        }
    }

    private async Task EnsureDeviceScopeIndexAsync(System.Data.Common.DbConnection connection, CancellationToken token)
    {
        using var listCommand = connection.CreateCommand();
        listCommand.CommandText = "SELECT name FROM sqlite_master WHERE type='index' AND tbl_name='Devices';";
        var indexes = new List<string>();
        using (var reader = await listCommand.ExecuteReaderAsync(token))
        {
            while (await reader.ReadAsync(token))
            {
                indexes.Add(reader.GetString(0));
            }
        }

        if (indexes.Contains("IX_Devices_NodeId_MacAddress_DiscoveryScopeId", StringComparer.OrdinalIgnoreCase))
            return;

        if (indexes.Contains("IX_Devices_NodeId_MacAddress", StringComparer.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Replacing legacy device index IX_Devices_NodeId_MacAddress with scope-aware composite index...");
            using var dropCommand = connection.CreateCommand();
            dropCommand.CommandText = "DROP INDEX IX_Devices_NodeId_MacAddress;";
            await dropCommand.ExecuteNonQueryAsync(token);
        }

        using var createCommand = connection.CreateCommand();
        createCommand.CommandText =
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_Devices_NodeId_MacAddress_DiscoveryScopeId " +
            "ON Devices(NodeId, MacAddress, DiscoveryScopeId) WHERE MacAddress IS NOT NULL;";
        await createCommand.ExecuteNonQueryAsync(token);
    }

    private async Task EnsureColumnExists(System.Data.Common.DbConnection connection, string tableName, string columnName, string columnType)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";
        
        var columns = new List<string>();
        using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                columns.Add(reader.GetString(1)); // Column name is the second column in the result set
            }
        }

        if (!columns.Contains(columnName, StringComparer.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Adding missing column '{Column}' to table '{Table}'...", columnName, tableName);
            using var alterCommand = connection.CreateCommand();
            alterCommand.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnType};";
            await alterCommand.ExecuteNonQueryAsync();
        }
    }
}
