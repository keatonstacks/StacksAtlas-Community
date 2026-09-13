using LiteDB;
using Microsoft.Extensions.Logging;
using StacksAtlas.Core.Abstractions;
using StacksAtlas.Core.Services.Security;

namespace StacksAtlas.Core.Services.Governance;

public interface ISnapshotService
{
    List<SnapshotInfo> GetAllSnapshots();
    CreateSnapshotResult CreateSnapshot(string label = "manual");
    void StageRestore(string fileName);
    void ExportPortable(string masterPassword, string destPath);
    void StageImportPortable(string sourcePath, string masterPassword);
    void FreshStart(FreshStartScope scope);
    void FreshStart(bool wipeFleetDatabase);
    void DeleteSnapshot(string fileName);
    bool HubDatabasePresent { get; }
}

public class SnapshotService : ISnapshotService
{
    private readonly string _dbPath;
    private readonly string _snapshotDir;
    private readonly LiteDatabase _db;
    private readonly DatabaseSecurityService _securityService;
    private readonly IClock _clock;
    private readonly ILogger<SnapshotService> _logger;

    /// <summary>Maximum number of snapshots retained. Oldest are pruned beyond this limit.</summary>
    private const int MaxSnapshots = 20;

    public SnapshotService(
        string dbPath,
        LiteDatabase db,
        DatabaseSecurityService securityService,
        IClock clock,
        ILogger<SnapshotService> logger)
    {
        _dbPath = dbPath;
        _snapshotDir = Path.Combine(Path.GetDirectoryName(dbPath)!, "Snapshots");
        _db = db;
        _securityService = securityService;
        _clock = clock;
        _logger = logger;
    }

    public bool HubDatabasePresent => FleetDatabaseGovernance.HubDatabaseExists(_dbPath);

    public List<SnapshotInfo> GetAllSnapshots()
    {
        if (!Directory.Exists(_snapshotDir))
            return [];

        return Directory.GetFiles(_snapshotDir, "*.db")
            .Select(f => new FileInfo(f))
            .Select(fi => new SnapshotInfo(
                fi.Name,
                fi.Length,
                fi.CreationTimeUtc,
                ClassifySnapshot(fi.Name)))
            .OrderByDescending(s => s.CreatedAt)
            .ToList();
    }

    public CreateSnapshotResult CreateSnapshot(string label = "manual")
    {
        Directory.CreateDirectory(_snapshotDir);

        var applianceFileName = CreateApplianceSnapshot(label);
        string? fleetFileName = null;

        if (HubDatabasePresent)
        {
            try
            {
                fleetFileName = FleetDatabaseGovernance.CreateHubSnapshot(
                    _dbPath, _snapshotDir, label, _clock.UtcNow);
                _logger.LogInformation("Created Hub fleet snapshot: {FileName}", fleetFileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create Hub fleet snapshot alongside appliance backup.");
                throw;
            }
        }

        PruneOldSnapshots();
        return new CreateSnapshotResult(applianceFileName, fleetFileName);
    }

    public void StageRestore(string fileName)
    {
        var safeName = SanitizeFileName(fileName);

        if (FleetDatabaseGovernance.IsHubSnapshotFileName(safeName))
        {
            if (!HubDatabasePresent)
            {
                throw new InvalidOperationException(
                    "Hub fleet snapshots can only be restored on a Hub appliance with a fleet database.");
            }

            FleetDatabaseGovernance.StageHubRestore(_dbPath, _snapshotDir, safeName);
            _logger.LogInformation("Hub fleet snapshot {FileName} staged for restore on next startup.", safeName);
            return;
        }

        if (!FleetDatabaseGovernance.IsApplianceSnapshotFileName(safeName))
            throw new ArgumentException("Unrecognized snapshot filename.");

        var sourcePath = Path.Combine(_snapshotDir, safeName);
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("Snapshot file not found.");

        var pendingPath = _dbPath + ".pending";
        File.Copy(sourcePath, pendingPath, true);

        _logger.LogInformation("Appliance snapshot {FileName} staged for restore on next startup.", safeName);
    }

    public void ExportPortable(string masterPassword, string destPath)
    {
        _logger.LogInformation("Creating portable database export...");

        var currentPassword = _securityService.GetDatabasePassword();

        _db.Checkpoint();
        File.Copy(_dbPath, destPath, true);

        try
        {
            using (var db = new LiteDatabase(new ConnectionString(destPath) { Password = currentPassword }))
            {
                db.Rebuild(new LiteDB.Engine.RebuildOptions { Password = masterPassword });
            }
        }
        catch
        {
            if (File.Exists(destPath))
                File.Delete(destPath);
            throw;
        }

        _logger.LogInformation("Portable database export created successfully at {Path}", destPath);
    }

    public void StageImportPortable(string sourcePath, string masterPassword)
    {
        _logger.LogInformation("Staging portable database import...");

        var pendingPath = _dbPath + ".pending";
        var machinePassword = _securityService.GetDatabasePassword();

        File.Copy(sourcePath, pendingPath, true);

        try
        {
            using (var sourceDb = new LiteDatabase(new ConnectionString(pendingPath) { Password = masterPassword }))
            {
                sourceDb.Rebuild(new LiteDB.Engine.RebuildOptions { Password = machinePassword });
            }
        }
        catch
        {
            if (File.Exists(pendingPath))
                File.Delete(pendingPath);
            throw;
        }

        _logger.LogInformation("Portable database staged for localized restore on next startup.");
    }

    public void FreshStart(bool wipeFleetDatabase) =>
        FreshStart(wipeFleetDatabase
            ? FreshStartScope.ApplianceAndFleet
            : FreshStartScope.ApplianceOnly);

    public void FreshStart(FreshStartScope scope)
    {
        ValidateScope(scope);

        BackupBeforeDestructiveScope(scope);

        var wipesAppliance = scope is FreshStartScope.ApplianceOnly
            or FreshStartScope.ApplianceAndFleet
            or FreshStartScope.FactoryReset;

        var wipesFleet = scope is FreshStartScope.FleetInventoryOnly
            or FreshStartScope.ApplianceAndFleet
            or FreshStartScope.FactoryReset;

        if (wipesFleet && HubDatabasePresent)
        {
            FleetDatabaseGovernance.StageHubFreshStart(_dbPath);
            _logger.LogWarning("Hub fleet database staged for wipe on next startup.");
        }
        else if (wipesFleet)
        {
            _logger.LogWarning(
                "Fresh Start scope {Scope} requested fleet wipe but no Hub fleet database exists.",
                scope);
        }

        if (wipesAppliance)
        {
            File.WriteAllText(_dbPath + ".freshstart", "TRUE");
            _logger.LogWarning("Appliance database staged for wipe on next startup.");
        }

        if (scope == FreshStartScope.FactoryReset)
        {
            FactorySettingsReset.Stage();
            _logger.LogWarning("Factory settings reset staged for next startup.");
        }

        _logger.LogWarning("Fresh Start scope {Scope} triggered.", scope);
    }

    public void DeleteSnapshot(string fileName)
    {
        var safeName = SanitizeFileName(fileName);
        var path = Path.Combine(_snapshotDir, safeName);
        if (File.Exists(path))
            File.Delete(path);
    }

    private void ValidateScope(FreshStartScope scope)
    {
        if (scope == FreshStartScope.FleetInventoryOnly && !HubDatabasePresent)
        {
            throw new InvalidOperationException(
                "Fleet inventory reset is only available when a Hub fleet database exists.");
        }
    }

    private void BackupBeforeDestructiveScope(FreshStartScope scope)
    {
        CreateApplianceSnapshot("Automatic_Before_FreshStart");

        if (HubDatabasePresent)
        {
            try
            {
                var hubSnapshot = FleetDatabaseGovernance.CreateHubSnapshot(
                    _dbPath, _snapshotDir, "Automatic_Before_FreshStart", _clock.UtcNow);
                _logger.LogInformation("Created Hub fleet safety snapshot: {FileName}", hubSnapshot);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to snapshot Hub fleet database before Fresh Start.");
                throw;
            }
        }
        else if (scope is FreshStartScope.FleetInventoryOnly or FreshStartScope.ApplianceAndFleet or FreshStartScope.FactoryReset)
        {
            _logger.LogDebug("Skipping Hub fleet safety snapshot  -  fleet database not present.");
        }

        PruneOldSnapshots();
    }

    private string CreateApplianceSnapshot(string label)
    {
        Directory.CreateDirectory(_snapshotDir);

        var cleanLabel = string.Join("_", label.Split(Path.GetInvalidFileNameChars()));
        var timestamp = _clock.UtcNow.ToString("yyyyMMdd_HHmmss");
        var fileName = $"StacksAtlas_{timestamp}_{cleanLabel}.db";
        var destPath = Path.Combine(_snapshotDir, fileName);

        _db.Checkpoint();
        File.Copy(_dbPath, destPath, true);

        _logger.LogInformation("Created appliance database snapshot: {FileName}", fileName);
        return fileName;
    }

    private static SnapshotStoreKind ClassifySnapshot(string fileName) =>
        FleetDatabaseGovernance.IsHubSnapshotFileName(fileName)
            ? SnapshotStoreKind.Fleet
            : SnapshotStoreKind.Appliance;

    private static string SanitizeFileName(string fileName)
    {
        var safe = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safe) || safe != fileName)
            throw new ArgumentException("Invalid snapshot filename.");
        return safe;
    }

    private void PruneOldSnapshots()
    {
        try
        {
            var snapshots = Directory.GetFiles(_snapshotDir, "*.db")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.CreationTimeUtc)
                .ToList();

            if (snapshots.Count <= MaxSnapshots)
                return;

            foreach (var old in snapshots.Skip(MaxSnapshots))
            {
                old.Delete();
                _logger.LogInformation("Pruned old snapshot: {Name}", old.Name);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to prune old snapshots.");
        }
    }
}

public record SnapshotInfo(
    string FileName,
    long SizeBytes,
    DateTime CreatedAt,
    SnapshotStoreKind Store = SnapshotStoreKind.Appliance);

public record CreateSnapshotResult(string ApplianceFileName, string? FleetFileName);
