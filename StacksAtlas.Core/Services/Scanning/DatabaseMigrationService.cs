using LiteDB;
using Microsoft.Extensions.Logging;
using StacksAtlas.Core.Services.Security;

namespace StacksAtlas.Core.Services.Scanning;

public interface IDatabaseMigrationService
{
    void RotateKey(string newPassword);
}

public class DatabaseMigrationService(ILogger<DatabaseMigrationService> logger, DatabaseSecurityService securityService, string dbPath) : IDatabaseMigrationService
{
    private readonly ILogger<DatabaseMigrationService> _logger = logger;
    private readonly DatabaseSecurityService _securityService = securityService;
    private readonly string _dbPath = dbPath;

    public void RotateKey(string newPassword)
    {
        if (string.IsNullOrWhiteSpace(newPassword))
            throw new ArgumentException("New password cannot be empty.", nameof(newPassword));

        var currentPassword = _securityService.GetDatabasePassword();
        var tempDbPath = _dbPath + ".rebuild";
        var backupPath = _dbPath + ".bak";

        _logger.LogInformation("Starting industrial-grade database key rotation...");

        try 
        {
            // 1. Preparation: Clean up any stale temp files
            if (File.Exists(tempDbPath)) File.Delete(tempDbPath);

            // 2. Clone the database for the rebuild process
            // We use File.Copy which works well with ConnectionType.Shared
            _logger.LogDebug("Cloning live database to temporary path for transformation...");
            File.Copy(_dbPath, tempDbPath, true);

            // 3. Transformation: Rebuild with the NEW password
            _logger.LogInformation("Applying new encryption key and rebuilding database structure...");
            using (var db = new LiteDatabase(new ConnectionString(tempDbPath) { Password = currentPassword }))
            {
                // LiteDB's Rebuild handles the heavy lifting of re-encrypting every page
                db.Rebuild(new LiteDB.Engine.RebuildOptions { Password = newPassword });
            }

            // 4. Verification: Ensure the new database is healthy and the key works
            _logger.LogDebug("Verifying integrity of the re-encrypted database...");
            using (var verifyDb = new LiteDatabase(new ConnectionString(tempDbPath) { Password = newPassword }))
            {
                // A simple query to force a read of the collection names (which are encrypted)
                var collections = verifyDb.GetCollectionNames().ToList();
                _logger.LogInformation("Integrity check passed. {Count} collections verified with new key.", collections.Count);
            }

            // 5. Atomic Swap: Replace the live DB with the transformed one
            _logger.LogInformation("Finalizing rotation: Swapping active database files...");
            
            // On Windows, if the file is locked, this may still fail. 
            // However, by moving the live file to .bak first, we clear the path for the new one.
            if (File.Exists(backupPath)) File.Delete(backupPath);
            
            try
            {
                File.Move(_dbPath, backupPath);
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, "Critical Lock: Could not move active database to backup. Rotation aborted to prevent data loss.");
                throw new InvalidOperationException("Database file is currently locked by another process. Please stop all background workers and try again.", ex);
            }

            try
            {
                File.Move(tempDbPath, _dbPath);
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "FATAL: Rebuilt database could not be moved into place. Attempting to restore original...");
                if (File.Exists(backupPath)) File.Move(backupPath, _dbPath);
                throw;
            }

            // 6. Update Vault: Persist the new key in the security service
            _securityService.SetDatabasePassword(newPassword);
            
            _logger.LogInformation("Database key rotation completed successfully. A system restart is recommended to refresh all connections.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database key rotation failed.");
            throw;
        }
        finally
        {
            // Always clean up the temp file
            if (File.Exists(tempDbPath))
            {
                try { File.Delete(tempDbPath); } catch { /* Ignore cleanup errors */ }
            }
        }
    }
}
