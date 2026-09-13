using Microsoft.Extensions.Logging.Abstractions;
using StacksAtlas.Core.Helpers;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Services.Governance;
using StacksAtlas.Core.Settings;

namespace StacksAtlas.Tests;

public class SiteIdentityBootstrapTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string? _previousDataDir;

    public SiteIdentityBootstrapTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "stacksatlas-site-id-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _previousDataDir = Environment.GetEnvironmentVariable("STACKSATLAS_DATADIR");
        Environment.SetEnvironmentVariable("STACKSATLAS_DATADIR", _tempDir);
        ResetPlatformPathsCache();
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("STACKSATLAS_DATADIR", _previousDataDir);
        ResetPlatformPathsCache();
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void TryApplySiteName_SetsNodeIdAndDisplayName_WhenUnnamedStandalone()
    {
        var store = new FederationSettingsStore(NullLogger<FederationSettingsStore>.Instance);
        Assert.Equal(SiteIdentityBootstrap.DefaultUnnamedNodeId, store.Current.NodeId);

        var applied = SiteIdentityBootstrap.TryApplySiteName(store, "New York Data Center");

        Assert.True(applied);
        Assert.Equal("new-york-data-center", store.Current.NodeId);
        Assert.Equal("New York Data Center", store.Current.NodeDisplayName);
    }

    [Fact]
    public void TryApplySiteName_DoesNotOverwriteNodeId_WhenEnrolled()
    {
        var store = new FederationSettingsStore(NullLogger<FederationSettingsStore>.Instance);
        var fed = store.Current;
        fed.NodeId = "hub-assigned-id";
        fed.HubUrl = "https://hub.example:5002";
        store.Save(fed);

        var applied = SiteIdentityBootstrap.TryApplySiteName(store, "Branch Office");

        Assert.True(applied);
        Assert.Equal("hub-assigned-id", store.Current.NodeId);
        Assert.Equal("Branch Office", store.Current.NodeDisplayName);
    }

    [Fact]
    public void SlugifyNodeId_NormalizesFriendlyNames()
    {
        Assert.Equal("main-office", SiteIdentityBootstrap.SlugifyNodeId("Main Office"));
        Assert.Equal(SiteIdentityBootstrap.DefaultUnnamedNodeId, SiteIdentityBootstrap.SlugifyNodeId("!!!"));
    }

    private static void ResetPlatformPathsCache()
    {
        var field = typeof(PlatformPaths).GetField(
            "_baseDataDir",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        field?.SetValue(null, null);
    }
}
