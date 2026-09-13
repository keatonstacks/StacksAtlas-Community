namespace StacksAtlas.Core.Settings;

public class OnboardingSettings
{
    /// <summary>True when the license-first bootstrap wizard (§7.7) has finished.</summary>
    public bool IsFoundationComplete { get; set; }

    /// <summary>Operator confirmed at least one scan scope (skipped for Hub-only role).</summary>
    public bool OnboardingNetworkConfirmed { get; set; }

    /// <summary>Human-friendly site label set during onboarding.</summary>
    public string SiteName { get; set; } = string.Empty;

    /// <summary>Set when legacy installs are auto-migrated on first boot after upgrade.</summary>
    public bool LegacyMigrationApplied { get; set; }

    /// <summary>First-run wizard in progress after /api/auth/setup  -  ignores stale pre-wipe onboarding metadata.</summary>
    public bool ExpressBootstrapPending { get; set; }

    /// <summary>Hub-initiated site reset recovery  -  abbreviated onboarding until cleared.</summary>
    public SiteResetRecoveryContext? PendingSiteResetRecovery { get; set; }
}
