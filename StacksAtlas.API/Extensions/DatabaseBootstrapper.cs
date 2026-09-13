using Serilog;
using StacksAtlas.Core.Helpers;
using StacksAtlas.Core.Settings;
using StacksAtlas.Core.Services.Governance;
using LiteDB;

namespace StacksAtlas.API.Extensions;

public static class DatabaseBootstrapper
{
    public static void PrepareDatabase(string baseDataDir, string databasePath)
    {
        PlatformPaths.EnsureDirectoriesExist();

        // Hub fleet restore must run before fresh-start wipe and before SQLite opens the file.
        FleetDatabaseGovernance.TryApplyHubPendingRestore(
            databasePath,
            msg => Log.Information(msg),
            ex => Log.Error(ex, "Failed to restore Hub fleet snapshot"));

        // Hub fleet wipe must run before any SQLite connections open the file.
        FleetDatabaseGovernance.TryApplyHubFreshStart(
            databasePath,
            msg => Log.Information(msg),
            ex => Log.Error(ex, "Failed to perform Hub fleet Fresh Start"));

        var siteResetFlag = SiteResetGovernance.GetSiteResetFlagPath(databasePath);
        var pendingRestore = databasePath + ".pending";
        var freshStartFlag = databasePath + ".freshstart";

        if (File.Exists(siteResetFlag))
        {
            try
            {
                SiteResetGovernance.TryApply(
                    databasePath,
                    msg => Log.Information(msg),
                    ex => Log.Error(ex, "Failed to perform Hub-initiated site reset"));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to perform Hub-initiated site reset");
            }
        }
        else if (File.Exists(freshStartFlag))
        {
            try {
                if (File.Exists(databasePath)) File.Delete(databasePath);
                File.Delete(freshStartFlag);
                OnboardingSettingsReset.ResetInSystemSettingsFile();
                Log.Information("Fresh Start applied. Appliance database wiped and onboarding flags reset.");
            } catch (Exception ex) {
                Log.Error(ex, "Failed to perform Fresh Start");
            }
        }
        else if (File.Exists(pendingRestore))
        {
            try {
                if (File.Exists(databasePath)) File.Delete(databasePath);
                File.Move(pendingRestore, databasePath);
                Log.Information("Appliance snapshot restoration applied.");
            } catch (Exception ex) {
                Log.Error(ex, "Failed to restore appliance snapshot");
            }
        }

        FactorySettingsReset.TryApply();
        
        // --- v1.3.0 Security Logic: Auto-Encrypt Legacy Databases ---
        PerformDatabaseSecurityCheck(databasePath);
        
        // --- v1.3.0 WAL Hardening ---
        // ONLY apply WAL to the Hub SQLite DB. LiteDB handles its own concurrency.
        var hubDbPath = Path.Combine(baseDataDir, "StacksAtlas.Hub.db");
        if (File.Exists(hubDbPath))
        {
            EnableWalMode(hubDbPath);
        }
    }

    private static void PerformDatabaseSecurityCheck(string databasePath)
    {
        if (!File.Exists(databasePath)) return;

        try
        {
            var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<StacksAtlas.Core.Services.Security.DatabaseSecurityService>.Instance;
            var securityService = new StacksAtlas.Core.Services.Security.DatabaseSecurityService(logger);
            var dbPassword = securityService.GetDatabasePassword();

            if (string.IsNullOrEmpty(dbPassword)) return;

            try
            {
                // Validation: Can we open with password?
                using var checkDb = new LiteDatabase(new ConnectionString(databasePath) { Password = dbPassword, Connection = ConnectionType.Shared });
                checkDb.GetCollection("sys_test").Count(); // Trigger access
            }
            catch (LiteException ex) when (ex.Message.Contains("File is not encrypted"))
            {
                Log.Warning("Detected unencrypted legacy database. Upgrading security...");
                try
                {
                    using (var unencryptedDb = new LiteDatabase(databasePath))
                    {
                        // Rebuild it WITH the password to encrypt it in-place
                        unencryptedDb.Rebuild(new LiteDB.Engine.RebuildOptions { Password = dbPassword });
                    }
                    Log.Information("SUCCESS: Database has been encrypted and upgraded.");
                }
                catch (Exception upgradeEx)
                {
                    Log.Error(upgradeEx, "CRITICAL: Failed to upgrade legacy database security.");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error checking database encryption state.");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize database security check.");
        }
    }

    private static void EnableWalMode(string dbPath)
    {
        try 
        {
            // Ensure we use a unique connection string to avoid pool conflicts during bootstrap
            var connectionString = $"Data Source={dbPath};Cache=Shared;";
            using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(connectionString))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=30000;";
                command.ExecuteNonQuery();
                connection.Close();
            }
            
            // Explicitly clear the pool to release the file handle immediately
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Log.Debug("SQLite WAL Mode enabled for: {Db}", Path.GetFileName(dbPath));
        }
        catch (Exception ex)
        {
            Log.Warning("Could not explicitly enable WAL for {Db}: {Msg}", Path.GetFileName(dbPath), ex.Message);
        }
    }
}
