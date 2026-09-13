using StacksAtlas.Core.Models;
using StacksAtlas.Core.Services.Federation;

namespace StacksAtlas.Tests;

public class HubEnrollmentEndpointResolverTests
{
    [Fact]
    public void GetEnrollmentString_DefaultsToLanHost_NotTailscale()
    {
        var settings = new FederationSettings
        {
            HubUrl = "https://192.168.1.50:5001",
            HubTailscaleMagicDns = "hub.example.ts.net"
        };

        var baseUrl = HubEnrollmentEndpointResolver.ResolveHubBaseUrl(settings, "https", "localhost:5001", () => "192.168.1.50");
        var lanHost = HubEnrollmentEndpointResolver.ResolveLanHost(settings, baseUrl);
        var tailscaleHost = HubEnrollmentEndpointResolver.ResolveTailscaleHost(settings);

        Assert.Equal("192.168.1.50", lanHost);
        Assert.Equal("hub.example.ts.net", tailscaleHost);

        var lanString = HubEnrollmentEndpointResolver.BuildEnrollmentConnectionString(lanHost, "token");
        Assert.StartsWith("sa-enroll://192.168.1.50:5002", lanString);
        Assert.DoesNotContain(".ts.net", lanString);
    }

    [Fact]
    public void ResolveHubBaseUrl_ReplacesLocalhostWithLanIp()
    {
        var settings = new FederationSettings { HubUrl = "https://localhost:5001" };
        var url = HubEnrollmentEndpointResolver.ResolveHubBaseUrl(settings, null, null, () => "10.0.0.5");
        Assert.Contains("10.0.0.5", url);
    }
}
