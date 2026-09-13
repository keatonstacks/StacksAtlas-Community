using Microsoft.Data.Sqlite;
using StacksAtlas.Core.Services.Governance;

namespace StacksAtlas.Tests;

public class FleetDatabaseGovernanceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _applianceDbPath;

    public FleetDatabaseGovernanceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "StacksAtlasTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _applianceDbPath = Path.Combine(_tempDir, "StacksAtlas.db");
        File.WriteAllText(_applianceDbPath, "lite");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // Best-effort cleanup on Windows file locks
        }
    }

    [Fact]
    public void FreshStartFlag_DeletesHubDatabaseAndSidecars()
    {
        var hubPath = FleetDatabaseGovernance.GetHubDatabasePath(_applianceDbPath);
        File.WriteAllText(hubPath, "hub");
        File.WriteAllText(hubPath + "-wal", "wal");
        File.WriteAllText(hubPath + "-shm", "shm");
        FleetDatabaseGovernance.StageHubFreshStart(_applianceDbPath);

        var applied = FleetDatabaseGovernance.TryApplyHubFreshStart(_applianceDbPath);

        Assert.True(applied);
        Assert.False(File.Exists(hubPath));
        Assert.False(File.Exists(hubPath + "-wal"));
        Assert.False(File.Exists(hubPath + "-shm"));
        Assert.False(File.Exists(hubPath + ".freshstart"));
    }

    [Fact]
    public void CreateHubSnapshot_CopiesDatabaseToSnapshotsFolder()
    {
        var hubPath = FleetDatabaseGovernance.GetHubDatabasePath(_applianceDbPath);
        CreateMinimalSqliteFile(hubPath);
        var snapshotDir = Path.Combine(_tempDir, "Snapshots");

        var fileName = FleetDatabaseGovernance.CreateHubSnapshot(
            _applianceDbPath, snapshotDir, "TestLabel", DateTime.UtcNow);

        Assert.StartsWith("StacksAtlas.Hub_", fileName);
        Assert.Contains("TestLabel", fileName);
        Assert.True(File.Exists(Path.Combine(snapshotDir, fileName)));
    }

    [Fact]
    public void HubPendingRestore_ReplacesFleetDatabase()
    {
        var hubPath = FleetDatabaseGovernance.GetHubDatabasePath(_applianceDbPath);
        CreateMinimalSqliteFile(hubPath);
        var snapshotDir = Path.Combine(_tempDir, "Snapshots");
        Directory.CreateDirectory(snapshotDir);

        var snapshotName = FleetDatabaseGovernance.CreateHubSnapshot(
            _applianceDbPath, snapshotDir, "RestoreTest", DateTime.UtcNow);

        File.WriteAllText(hubPath, "stale-fleet-data");

        FleetDatabaseGovernance.StageHubRestore(_applianceDbPath, snapshotDir, snapshotName);

        var applied = FleetDatabaseGovernance.TryApplyHubPendingRestore(_applianceDbPath);

        Assert.True(applied);
        Assert.True(File.Exists(hubPath));
        Assert.NotEqual("stale-fleet-data", File.ReadAllText(hubPath));
        Assert.False(File.Exists(hubPath + ".pending"));
    }

    [Fact]
    public void SnapshotFileName_Classification()
    {
        Assert.True(FleetDatabaseGovernance.IsHubSnapshotFileName("StacksAtlas.Hub_20260530_120000_Test.db"));
        Assert.True(FleetDatabaseGovernance.IsApplianceSnapshotFileName("StacksAtlas_20260530_120000_Test.db"));
        Assert.False(FleetDatabaseGovernance.IsApplianceSnapshotFileName("StacksAtlas.Hub_20260530_Test.db"));
    }

    private static void CreateMinimalSqliteFile(string path)
    {
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS probe (id INTEGER PRIMARY KEY);";
        command.ExecuteNonQuery();
    }
}
