using StacksAtlas.API.Controllers;
using Xunit;

namespace StacksAtlas.Tests;

public class DirectPushEnrollmentTests
{
    [Fact]
    public void BuildNodeUrlCandidates_HttpsWithoutPort_IncludesDockerDefault()
    {
        var candidates = FederationController.BuildNodeUrlCandidates("https://192.168.1.239");

        Assert.Contains("https://192.168.1.239", candidates);
        Assert.Contains("http://192.168.1.239:5000", candidates);
    }

    [Fact]
    public void BuildNodeUrlCandidates_DockerUrl_StaysSingleCandidate()
    {
        var candidates = FederationController.BuildNodeUrlCandidates("http://192.168.1.239:5000");

        Assert.Equal(["http://192.168.1.239:5000"], candidates);
    }

    [Fact]
    public void BuildNodeUrlCandidates_BareIp_AddsHttpSchemeAndPort5000()
    {
        var candidates = FederationController.BuildNodeUrlCandidates("192.168.1.239");

        Assert.Contains("http://192.168.1.239", candidates);
        Assert.Contains("http://192.168.1.239:5000", candidates);
    }
}
