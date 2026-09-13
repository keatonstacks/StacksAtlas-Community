using Microsoft.Extensions.Logging;
using StacksAtlas.Core.Services.Auth;
using StacksAtlas.Core.Settings;
using StacksAtlas.Core.State;

namespace StacksAtlas.API.Services;

/// <summary>
/// Seeds portable evaluation appliances with an internal admin account, Home-tier license,
/// and default site metadata  -  no operator account or license key required.
/// </summary>
public static class PortableOnboardingBootstrap
{
    public const string EvalUsername = "evaluation";
    public const string DefaultSiteName = "Portable";

    public static void ApplyIfNeeded(
        SystemSettingsStore settingsStore,
        IAuthService authService,
        ILogger logger)
    {
        if (!ExecutionState.IsPortable)
            return;

        if (!authService.AnyUsers())
        {
            var password = Guid.NewGuid().ToString("N");
            var user = authService.RegisterAdmin(EvalUsername, password);
            if (user == null)
            {
                logger.LogWarning("Portable evaluation bootstrap skipped  -  admin user already exists.");
                return;
            }

            logger.LogInformation(
                "Portable evaluation: internal admin account provisioned (no password required  -  use portable session).");
        }

        var settings = settingsStore.Load();
        var onboarding = settings.Onboarding;

        if (string.IsNullOrWhiteSpace(onboarding.SiteName))
            onboarding.SiteName = DefaultSiteName;

        settings.Onboarding = onboarding;
        settingsStore.Save(settings);

        logger.LogInformation(
            "Portable evaluation bootstrap ready  -  site \"{SiteName}\", Home tier, federation disabled.",
            onboarding.SiteName);
    }
}
