using Microsoft.Data.Sqlite;

namespace StacksAtlas.Core.Services.Governance;

/// <summary>
/// File-level operations for the Hub fleet SQLite store (StacksAtlas.Hub.db).
/// </summary>
public static class FleetDatabaseGovernance
{
    public const string HubFileName = "StacksAtlas.Hub.db";

    public static string GetHubDatabasePath(string applianceDatabasePath) =>
        Path.Combine(Path.GetDirectoryName(applianceDatabasePath)!, HubFileName);

    public static bool HubDatabaseExists(string applianceDatabasePath) =>
        File.Exists(GetHubDatabasePath(applianceDatabasePath));

    /// <summary>Checkpoint WAL and copy Hub.db into Snapshots/.</summary>
    public static string CreateHubSnapshot(string applianceDatabasePath, string snapshotDir, string label, DateTime timestampUtc)
    {
        var hubDbPath = GetHubDatabasePath(applianceDatabasePath);
        if (!File.Exists(hubDbPath))
            throw new FileNotFoundException("Hub fleet database not found.", hubDbPath);

        Directory.CreateDirectory(snapshotDir);

        var cleanLabel = string.Join("_", label.Split(Path.GetInvalidFileNameChars()));
        var timestamp = timestampUtc.ToString("yyyyMMdd_HHmmss");
        var fileName = $"StacksAtlas.Hub_{timestamp}_{cleanLabel}.db";
        var destPath = Path.Combine(snapshotDir, fileName);

        CheckpointAndCopy(hubDbPath, destPath);
        return fileName;
    }

    public static void StageHubFreshStart(string applianceDatabasePath)
    {
        var flagPath = GetHubDatabasePath(applianceDatabasePath) + ".freshstart";
        File.WriteAllText(flagPath, "TRUE");
    }

    public static bool TryApplyHubFreshStart(string applianceDatabasePath, Action<string>? logInfo = null, Action<Exception>? logError = null)
    {
        var hubDbPath = GetHubDatabasePath(applianceDatabasePath);
        var flagPath = hubDbPath + ".freshstart";
        if (!File.Exists(flagPath)) return false;

        try
        {
            DeleteHubDatabaseFiles(hubDbPath);
            File.Delete(flagPath);
            logInfo?.Invoke("Fresh Start applied. Hub fleet database wiped.");
            return true;
        }
        catch (Exception ex)
        {
            logError?.Invoke(ex);
            return false;
        }
    }

    public static void DeleteHubDatabaseFiles(string hubDbPath)
    {
        SqliteConnection.ClearAllPools();

        foreach (var path in new[] { hubDbPath, hubDbPath + "-wal", hubDbPath + "-shm" })
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    public static bool IsHubSnapshotFileName(string fileName) =>
        fileName.StartsWith("StacksAtlas.Hub_", StringComparison.OrdinalIgnoreCase)
        && fileName.EndsWith(".db", StringComparison.OrdinalIgnoreCase);

    public static bool IsApplianceSnapshotFileName(string fileName) =>
        fileName.StartsWith("StacksAtlas_", StringComparison.OrdinalIgnoreCase)
        && !IsHubSnapshotFileName(fileName)
        && fileName.EndsWith(".db", StringComparison.OrdinalIgnoreCase);

    public static void StageHubRestore(string applianceDatabasePath, string snapshotDir, string fileName)
    {
        var safeName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeName) || safeName != fileName || !IsHubSnapshotFileName(safeName))
            throw new ArgumentException("Invalid Hub snapshot filename.");

        var sourcePath = Path.Combine(snapshotDir, safeName);
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("Hub snapshot file not found.", sourcePath);

        var hubDbPath = GetHubDatabasePath(applianceDatabasePath);
        var pendingPath = hubDbPath + ".pending";
        File.Copy(sourcePath, pendingPath, overwrite: true);
    }

    public static bool TryApplyHubPendingRestore(
        string applianceDatabasePath,
        Action<string>? logInfo = null,
        Action<Exception>? logError = null)
    {
        var hubDbPath = GetHubDatabasePath(applianceDatabasePath);
        var pendingPath = hubDbPath + ".pending";
        if (!File.Exists(pendingPath))
            return false;

        try
        {
            DeleteHubDatabaseFiles(hubDbPath);
            File.Move(pendingPath, hubDbPath);
            logInfo?.Invoke("Hub fleet snapshot restoration applied.");
            return true;
        }
        catch (Exception ex)
        {
            logError?.Invoke(ex);
            return false;
        }
    }

    private static void CheckpointAndCopy(string hubDbPath, string destPath)
    {
        var connectionString = $"Data Source={hubDbPath};Cache=Shared;";
        using (var connection = new SqliteConnection(connectionString))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA wal_checkpoint(FULL);";
            command.ExecuteNonQuery();
            connection.Close();
        }

        SqliteConnection.ClearAllPools();
        File.Copy(hubDbPath, destPath, overwrite: true);
    }
}
