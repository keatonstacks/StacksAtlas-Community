using StacksAtlas.Core.Services.Governance;
using StacksAtlas.Core.Settings;

namespace StacksAtlas.Tests;

public class SiteResetGovernanceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;

    public SiteResetGovernanceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "StacksAtlasSiteReset_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "StacksAtlas.db");
        File.WriteAllText(_dbPath, "lite");
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
    public void TryApply_WipesApplianceDatabase_AndRemovesFlag()
    {
        SiteResetGovernance.Stage(_dbPath);
        Assert.True(File.Exists(SiteResetGovernance.GetSiteResetFlagPath(_dbPath)));

        var applied = SiteResetGovernance.TryApply(_dbPath);

        Assert.True(applied);
        Assert.False(File.Exists(_dbPath));
        Assert.False(File.Exists(SiteResetGovernance.GetSiteResetFlagPath(_dbPath)));
    }

    [Fact]
    public void PersistRecoveryContext_WritesPendingRecoveryToSystemSettings()
    {
        var previous = Environment.GetEnvironmentVariable("STACKSATLAS_DATADIR");
        Environment.SetEnvironmentVariable("STACKSATLAS_DATADIR", _tempDir);

        try
        {
            var store = new SystemSettingsStore(Microsoft.Extensions.Logging.Abstractions.NullLogger<SystemSettingsStore>.Instance);
            SiteResetGovernance.PersistRecoveryContext(
                store,
                new SiteResetRecoveryContext
                {
                    HubInitiated = true,
                    InitiatedByUsername = "admin",
                    InitiatedAtUtc = new DateTime(2026, 5, 24, 12, 0, 0, DateTimeKind.Utc),
                    HubDisplayName = "HQ Hub"
                });

            var reloaded = store.Load();
            Assert.NotNull(reloaded.Onboarding.PendingSiteResetRecovery);
            Assert.True(reloaded.Onboarding.PendingSiteResetRecovery!.HubInitiated);
            Assert.Equal("admin", reloaded.Onboarding.PendingSiteResetRecovery.InitiatedByUsername);
            Assert.True(reloaded.Onboarding.ExpressBootstrapPending);
        }
        finally
        {
            Environment.SetEnvironmentVariable("STACKSATLAS_DATADIR", previous);
        }
    }
}
