using StacksAtlas.Core.Services.Governance;

namespace StacksAtlas.Core.Database;

/// <summary>
/// File-level size metrics for appliance (LiteDB) and optional fleet (Hub SQLite) stores.
/// </summary>
public static class DatabaseStorageMetrics
{
    public sealed record Snapshot(
        long ApplianceSizeBytes,
        long FleetSizeBytes,
        bool HasFleetDatabase,
        string ApplianceFileName,
        string FleetFileName);

    public static Snapshot Read(string applianceDatabasePath)
    {
        long applianceSize = 0;
        if (File.Exists(applianceDatabasePath))
            applianceSize = new FileInfo(applianceDatabasePath).Length;

        var hasFleet = FleetDatabaseGovernance.HubDatabaseExists(applianceDatabasePath);
        long fleetSize = 0;
        if (hasFleet)
        {
            var hubPath = FleetDatabaseGovernance.GetHubDatabasePath(applianceDatabasePath);
            if (File.Exists(hubPath))
                fleetSize = new FileInfo(hubPath).Length;
        }

        return new Snapshot(
            applianceSize,
            fleetSize,
            hasFleet,
            Path.GetFileName(applianceDatabasePath),
            FleetDatabaseGovernance.HubFileName);
    }

    public static string FormatFleetEngine(string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
            return "SQLite";

        return provider.ToLowerInvariant() switch
        {
            "sqlite" => "SQLite",
            "postgresql" or "postgres" => "PostgreSQL",
            "sqlserver" or "mssql" => "SQL Server",
            _ => provider
        };
    }
}
