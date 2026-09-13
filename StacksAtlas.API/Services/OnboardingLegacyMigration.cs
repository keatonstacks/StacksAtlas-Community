using Microsoft.Extensions.Logging;
using StacksAtlas.Core.Services.Auth;
using StacksAtlas.Core.Settings;
using StacksAtlas.Core.State;

namespace StacksAtlas.API.Services;

/// <summary>
/// One-time migration for appliances that existed before license-first onboarding (§7.7).
/// </summary>
public static class OnboardingLegacyMigration
{
    public static void ApplyIfNeeded(
        SystemSettingsStore settingsStore,
        IAuthService authService,
        ILogger logger)
    {
        // Portable creates an internal admin on first boot  -  that is not a legacy appliance.
        if (ExecutionState.IsPortable)
            return;

        if (!authService.AnyUsers())
            return;

        var settings = settingsStore.Load();
        var onboarding = settings.Onboarding;
        if (onboarding.IsFoundationComplete || onboarding.LegacyMigrationApplied)
            return;

        // Fresh license-first setup creates the admin, then BeginExpressBootstrap marks the wizard
        // in-flight. Do not grandfather that path as a pre-onboarding upgrade (would skip license /
        // network steps and leave discovery gated or empty-scoped).
        if (onboarding.ExpressBootstrapPending)
            return;

        onboarding.IsFoundationComplete = true;
        onboarding.OnboardingNetworkConfirmed = true;
        onboarding.LegacyMigrationApplied = true;

        if (string.IsNullOrWhiteSpace(onboarding.SiteName))
            onboarding.SiteName = "Default Site";

        settings.Onboarding = onboarding;
        settingsStore.Save(settings);
        logger.LogInformation(
            "Onboarding legacy migration applied  -  existing appliance grandfathered into foundation-complete state.");
    }
}
