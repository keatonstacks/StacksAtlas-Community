using System.Text.Json;
using Microsoft.Extensions.Logging;
using StacksAtlas.Core.Helpers;

namespace StacksAtlas.Core.Settings;

/// <summary>
/// Keeps onboarding flags aligned with database state (e.g. after Fresh Start).
/// </summary>
public static class OnboardingSettingsReset
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>True when onboarding metadata implies a prior bootstrap that no longer matches an empty user store.</summary>
    public static bool HasStaleOnboardingFlags(OnboardingSettings onboarding) =>
        onboarding.IsFoundationComplete
        || onboarding.LegacyMigrationApplied
        || onboarding.OnboardingNetworkConfirmed
        || !string.IsNullOrWhiteSpace(onboarding.SiteName);

    /// <summary>Resets onboarding section in systemsettings.json (license and other settings preserved).</summary>
    public static void ResetInSystemSettingsFile(ILogger? logger = null)
    {
        var path = PlatformPaths.GetSystemSettingsPath();
        var dir = Path.GetDirectoryName(path);
        if (dir != null) Directory.CreateDirectory(dir);

        SystemSettings settings;
        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                settings = JsonSerializer.Deserialize<SystemSettings>(json, JsonOptions) ?? new SystemSettings();
            }
            else
            {
                settings = new SystemSettings();
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Could not read system settings for onboarding reset; using defaults.");
            settings = new SystemSettings();
        }

        settings.Onboarding = new OnboardingSettings();
        WriteSettings(path, settings);
        logger?.LogInformation("Onboarding flags reset in system settings.");
    }

    /// <summary>
    /// When LiteDB has no users but settings still say foundation-complete, repair persisted flags.
    /// Returns effective foundation-complete for API responses and route gates.
    /// </summary>
    public static bool GetEffectiveFoundationComplete(OnboardingSettings onboarding, bool hasUsers)
    {
        if (!hasUsers)
            return false;

        // Completed bootstrap wins over a stale in-flight flag (crash mid-save recovery).
        if (onboarding.IsFoundationComplete || onboarding.LegacyMigrationApplied)
            return true;

        if (onboarding.ExpressBootstrapPending)
            return false;

        return false;
    }

    /// <summary>Clears stale ExpressBootstrapPending when foundation was already completed.</summary>
    public static bool TryHealCompletedBootstrapFlags(SystemSettingsStore store, bool hasUsers, ILogger? logger = null)
    {
        if (!hasUsers)
            return false;

        var settings = store.Load();
        var onboarding = settings.Onboarding;
        if (!onboarding.ExpressBootstrapPending
            || !(onboarding.IsFoundationComplete || onboarding.LegacyMigrationApplied))
        {
            return false;
        }

        onboarding.ExpressBootstrapPending = false;
        settings.Onboarding = onboarding;
        store.Save(settings);
        logger?.LogInformation(
            "Cleared stale ExpressBootstrapPending  -  foundation bootstrap was already complete.");
        return true;
    }

    /// <summary>Marks the license-first wizard as in-flight and clears stale pre-wipe metadata.</summary>
    public static void BeginExpressBootstrap(SystemSettingsStore store, ILogger? logger = null)
    {
        var settings = store.Load();
        settings.Onboarding = new OnboardingSettings { ExpressBootstrapPending = true };
        store.Save(settings);
        logger?.LogInformation("Express onboarding bootstrap started  -  wizard metadata reset.");
    }

    /// <summary>
    /// When LiteDB has no users but settings still say foundation-complete, repair persisted flags.
    /// </summary>
    public static bool ReconcileIfNoUsers(
        SystemSettingsStore store,
        bool hasUsers,
        ILogger? logger = null,
        bool databaseReady = true)
    {
        if (!databaseReady)
            return GetEffectiveFoundationComplete(store.Load().Onboarding, hasUsers);

        if (hasUsers)
        {
            TryHealCompletedBootstrapFlags(store, hasUsers, logger);
            return GetEffectiveFoundationComplete(store.Load().Onboarding, hasUsers: true);
        }

        // Never wipe persisted onboarding flags here. Fresh Start and site-reset governance
        // call ResetInSystemSettingsFile() explicitly; auto-clearing on status polls caused
        // restart loops when AnyUsers() was briefly false during bootstrap.
        return false;
    }

    private static void WriteSettings(string path, SystemSettings settings)
    {
        var tempPath = path + ".tmp";
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, path, overwrite: true);
    }
}
