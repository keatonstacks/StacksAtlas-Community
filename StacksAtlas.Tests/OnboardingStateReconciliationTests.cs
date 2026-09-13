using Microsoft.Extensions.Logging.Abstractions;
using StacksAtlas.Core.Settings;

namespace StacksAtlas.Tests;

public class OnboardingStateReconciliationTests
{
    [Fact]
    public void HasStaleOnboardingFlags_Detects_FoundationComplete_Without_Users()
    {
        var onboarding = new OnboardingSettings { IsFoundationComplete = true };
        Assert.True(OnboardingSettingsReset.HasStaleOnboardingFlags(onboarding));
    }

    [Fact]
    public void ReconcileIfNoUsers_Preserves_Stale_Flags_When_NoUsers()
    {
        var dir = Path.Combine(Path.GetTempPath(), "stacksatlas-onboarding-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var previous = Environment.GetEnvironmentVariable("STACKSATLAS_DATADIR");
        Environment.SetEnvironmentVariable("STACKSATLAS_DATADIR", dir);

        try
        {
            var store = new SystemSettingsStore(NullLogger<SystemSettingsStore>.Instance);
            var settings = store.Load();
            settings.Onboarding.IsFoundationComplete = true;
            settings.Onboarding.LegacyMigrationApplied = true;
            settings.Onboarding.SiteName = "Default Site";
            settings.Onboarding.OnboardingNetworkConfirmed = true;
            store.Save(settings);

            var effective = OnboardingSettingsReset.ReconcileIfNoUsers(store, hasUsers: false);

            Assert.False(effective);
            var reloaded = store.Load();
            Assert.True(reloaded.Onboarding.IsFoundationComplete);
            Assert.True(reloaded.Onboarding.LegacyMigrationApplied);
            Assert.Equal("Default Site", reloaded.Onboarding.SiteName);
            Assert.True(reloaded.Onboarding.OnboardingNetworkConfirmed);
        }
        finally
        {
            Environment.SetEnvironmentVariable("STACKSATLAS_DATADIR", previous);
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void GetEffectiveFoundationComplete_IsTrue_When_FoundationComplete_EvenIfExpressBootstrapPendingStale()
    {
        var onboarding = new OnboardingSettings
        {
            IsFoundationComplete = true,
            ExpressBootstrapPending = true,
        };

        Assert.True(OnboardingSettingsReset.GetEffectiveFoundationComplete(onboarding, hasUsers: true));
    }

    [Fact]
    public void ReconcileIfNoUsers_SkipsDestructiveReset_WhenDatabaseNotReady()
    {
        var dir = Path.Combine(Path.GetTempPath(), "stacksatlas-onboarding-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var previous = Environment.GetEnvironmentVariable("STACKSATLAS_DATADIR");
        Environment.SetEnvironmentVariable("STACKSATLAS_DATADIR", dir);

        try
        {
            var store = new SystemSettingsStore(NullLogger<SystemSettingsStore>.Instance);
            var settings = store.Load();
            settings.Onboarding.IsFoundationComplete = true;
            settings.Onboarding.SiteName = "Natick Apartment";
            store.Save(settings);

            var effective = OnboardingSettingsReset.ReconcileIfNoUsers(store, hasUsers: false, databaseReady: false);

            Assert.False(effective);
            var reloaded = store.Load().Onboarding;
            Assert.True(reloaded.IsFoundationComplete);
            Assert.Equal("Natick Apartment", reloaded.SiteName);
        }
        finally
        {
            Environment.SetEnvironmentVariable("STACKSATLAS_DATADIR", previous);
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void GetEffectiveFoundationComplete_IsFalse_When_ExpressBootstrapInFlight()
    {
        var onboarding = new OnboardingSettings
        {
            IsFoundationComplete = false,
            ExpressBootstrapPending = true,
        };

        Assert.False(OnboardingSettingsReset.GetEffectiveFoundationComplete(onboarding, hasUsers: true));
    }

    [Fact]
    public void BeginExpressBootstrap_Clears_Stale_Metadata_And_Marks_Wizard_InFlight()
    {
        var dir = Path.Combine(Path.GetTempPath(), "stacksatlas-onboarding-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var previous = Environment.GetEnvironmentVariable("STACKSATLAS_DATADIR");
        Environment.SetEnvironmentVariable("STACKSATLAS_DATADIR", dir);

        try
        {
            var store = new SystemSettingsStore(NullLogger<SystemSettingsStore>.Instance);
            var settings = store.Load();
            settings.Onboarding.IsFoundationComplete = true;
            settings.Onboarding.LegacyMigrationApplied = true;
            settings.Onboarding.SiteName = "Default Site";
            settings.Onboarding.OnboardingNetworkConfirmed = true;
            store.Save(settings);

            OnboardingSettingsReset.BeginExpressBootstrap(store);

            var reloaded = store.Load().Onboarding;
            Assert.True(reloaded.ExpressBootstrapPending);
            Assert.False(reloaded.IsFoundationComplete);
            Assert.False(reloaded.LegacyMigrationApplied);
            Assert.Equal(string.Empty, reloaded.SiteName);
            Assert.False(reloaded.OnboardingNetworkConfirmed);
        }
        finally
        {
            Environment.SetEnvironmentVariable("STACKSATLAS_DATADIR", previous);
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }
}
