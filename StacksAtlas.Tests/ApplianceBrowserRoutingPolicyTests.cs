using System.Net;
using StacksAtlas.Core.Services.Security;
using Xunit;

namespace StacksAtlas.Tests;

public class ApplianceBrowserRoutingPolicyTests
{
    [Theory]
    [InlineData("/api/federation/enroll")]
    [InlineData("/api/federation/realtime/negotiate")]
    [InlineData("/api/federation/pulse")]
    [InlineData("/api/health")]
    [InlineData("/api/devices")]
    public void ShouldBypassSchemeRouting_ApiPaths_NeverRedirect(string path)
    {
        Assert.True(ApplianceBrowserRoutingPolicy.ShouldBypassSchemeRouting(path, IPAddress.Parse("192.168.1.10")));
        Assert.True(ApplianceBrowserRoutingPolicy.ShouldBypassSchemeRouting(path, IPAddress.Loopback));
    }

    [Fact]
    public void ShouldBypassSchemeRouting_RemoteDashboardPages_NotRedirected()
    {
        Assert.True(ApplianceBrowserRoutingPolicy.ShouldBypassSchemeRouting("/onboarding", IPAddress.Parse("192.168.1.10")));
        Assert.True(ApplianceBrowserRoutingPolicy.ShouldBypassSchemeRouting("/", IPAddress.Parse("10.0.0.5")));
    }

    [Fact]
    public void ShouldApplySchemeRouting_LocalBrowserPagesOnly()
    {
        Assert.False(ApplianceBrowserRoutingPolicy.ShouldBypassSchemeRouting("/onboarding", IPAddress.Loopback));
        Assert.False(ApplianceBrowserRoutingPolicy.ShouldBypassSchemeRouting("/", IPAddress.Loopback));
    }

    [Theory]
    [InlineData("/api/federation/enroll")]
    [InlineData("/api/federation/pulse")]
    [InlineData("/api/health")]
    public void IsBootstrapApiPath_FederationAvailableDuringDbInit(string path)
    {
        Assert.True(ApplianceBrowserRoutingPolicy.IsBootstrapApiPath(path));
    }
}
