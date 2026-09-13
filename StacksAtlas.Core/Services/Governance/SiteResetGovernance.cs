using Microsoft.Extensions.Logging;
using StacksAtlas.Core.Settings;
using StacksAtlas.Core.Services.Governance;

namespace StacksAtlas.Core.Services.Governance;

/// <summary>
/// §7.10 C.1  -  Hub-initiated remote site reset with enrollment preserved.
/// </summary>
public static class SiteResetGovernance
{
    public const string SiteResetFlagSuffix = ".siterest";

    public static string GetSiteResetFlagPath(string applianceDatabasePath) =>
        applianceDatabasePath + SiteResetFlagSuffix;

    public static void Stage(string applianceDatabasePath) =>
        File.WriteAllText(GetSiteResetFlagPath(applianceDatabasePath), "TRUE");

    public static bool TryApply(string applianceDatabasePath, Action<string>? logInfo = null, Action<Exception>? logError = null)
    {
        var flagPath = GetSiteResetFlagPath(applianceDatabasePath);
        if (!File.Exists(flagPath))
            return false;

        try
        {
            DeleteApplianceDatabase(applianceDatabasePath);
            File.Delete(flagPath);
            logInfo?.Invoke(
                "Hub-initiated site reset applied. Appliance database wiped; federation enrollment preserved.");
            return true;
        }
        catch (Exception ex)
        {
            logError?.Invoke(ex);
            return false;
        }
    }

    public static void PersistRecoveryContext(
        SystemSettingsStore store,
        SiteResetRecoveryContext context,
        FederationSettingsStore? federationSettingsStore = null,
        ILogger? logger = null)
    {
        var settings = store.Load();

        if (string.IsNullOrWhiteSpace(settings.Onboarding.SiteName) && federationSettingsStore != null)
        {
            var fed = federationSettingsStore.Current;
            if (!string.IsNullOrWhiteSpace(fed.NodeDisplayName))
                settings.Onboarding.SiteName = fed.NodeDisplayName.Trim();
            else if (!string.IsNullOrWhiteSpace(fed.NodeId))
                settings.Onboarding.SiteName = fed.NodeId;
        }

        settings.Onboarding.PendingSiteResetRecovery = context;
        settings.Onboarding.ExpressBootstrapPending = true;
        store.Save(settings);
        logger?.LogInformation(
            "Site reset recovery context persisted for Hub-initiated reset by {InitiatedBy}.",
            context.InitiatedByUsername);
    }

    public static bool IsHubSignalRDisconnectDuringReset(Exception ex)
    {
        for (var current = ex; current != null; current = current.InnerException)
        {
            if (current is IOException io &&
                io.Message.Contains("disconnected", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static void ClearRecoveryContext(SystemSettingsStore store, ILogger? logger = null)
    {
        var settings = store.Load();
        if (settings.Onboarding.PendingSiteResetRecovery == null)
            return;

        settings.Onboarding.PendingSiteResetRecovery = null;
        settings.Onboarding.ExpressBootstrapPending = false;
        store.Save(settings);
        logger?.LogInformation("Site reset recovery context cleared after onboarding completion.");
    }

    private static void DeleteApplianceDatabase(string databasePath)
    {
        foreach (var path in GetApplianceSidecarPaths(databasePath))
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static IEnumerable<string> GetApplianceSidecarPaths(string databasePath) =>
    [
        databasePath,
        databasePath + "-wal",
        databasePath + "-shm",
        databasePath + ".pending"
    ];
}
