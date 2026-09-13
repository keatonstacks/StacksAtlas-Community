using StacksAtlas.Core.Settings;
using StacksAtlas.Core.Services.Governance;
using Xunit;

namespace StacksAtlas.Tests;

public class FederatedNodeStatusTests
{
    [Fact]
    public void RequiresOperatorSetup_IsTrue_When_NoUsers()
    {
        var onboarding = new OnboardingSettings { IsFoundationComplete = true };
        Assert.True(FederatedNodeStatus.RequiresOperatorSetup(onboarding, hasUsers: false));
    }

    [Fact]
    public void RequiresOperatorSetup_IsTrue_When_ExpressBootstrapPendingWithoutFoundation()
    {
        var onboarding = new OnboardingSettings { ExpressBootstrapPending = true };
        Assert.True(FederatedNodeStatus.RequiresOperatorSetup(onboarding, hasUsers: true));
    }

    [Fact]
    public void RequiresOperatorSetup_IsFalse_When_ExpressBootstrapPendingButFoundationComplete()
    {
        var onboarding = new OnboardingSettings { ExpressBootstrapPending = true, IsFoundationComplete = true };
        Assert.False(FederatedNodeStatus.RequiresOperatorSetup(onboarding, hasUsers: true));
    }

    [Fact]
    public void ResolveAfterOperationalPulse_ClearsSetupRequired_WhenNodeReportsOperational()
    {
        var updated = FederatedNodeStatus.ResolveAfterOperationalPulse(
            FederatedNodeStatus.SetupRequired,
            requiresOperatorSetup: false);
        Assert.Equal(FederatedNodeStatus.Online, updated);
    }

    [Fact]
    public void ResolveAfterOperationalPulse_PromotesToSetupRequired_WhenNodeReportsIncomplete()
    {
        var updated = FederatedNodeStatus.ResolveAfterOperationalPulse(
            FederatedNodeStatus.Online,
            requiresOperatorSetup: true);
        Assert.Equal(FederatedNodeStatus.SetupRequired, updated);
    }

    [Fact]
    public void RequiresOperatorSetup_IsFalse_When_FoundationCompleteAndUsersExist()
    {
        var onboarding = new OnboardingSettings { IsFoundationComplete = true };
        Assert.False(FederatedNodeStatus.RequiresOperatorSetup(onboarding, hasUsers: true));
    }

    [Fact]
    public void ResolveFromRegistration_ReturnsSetupRequired_When_FlagSet()
    {
        Assert.Equal(FederatedNodeStatus.SetupRequired, FederatedNodeStatus.ResolveFromRegistration(requiresOperatorSetup: true));
        Assert.Equal(FederatedNodeStatus.Online, FederatedNodeStatus.ResolveFromRegistration(requiresOperatorSetup: false));
    }

    [Theory]
    [InlineData("setup_required", true)]
    [InlineData("SETUP_REQUIRED", true)]
    [InlineData("resetting", true)]
    [InlineData("online", false)]
    [InlineData("offline", false)]
    [InlineData(null, false)]
    public void ShouldPreserveOnHeartbeat_OnlyForNonOperationalStates(string? status, bool expected)
    {
        Assert.Equal(expected, FederatedNodeStatus.ShouldPreserveOnHeartbeat(status));
    }

    [Fact]
    public void ResolveEffectiveStatus_ReturnsOffline_WhenNotConnectedAndStale()
    {
        var node = new StacksAtlas.Core.Models.FederatedNode
        {
            Id = "mac-intel",
            Status = FederatedNodeStatus.Online,
            ConnectionId = "dead-conn",
            LastSeenUtc = DateTime.UtcNow.AddMinutes(-10),
        };

        var effective = FederatedNodeStatus.ResolveEffectiveStatus(node, signalRConnected: false, DateTime.UtcNow);
        Assert.Equal(FederatedNodeStatus.Offline, effective);
    }

    [Fact]
    public void ResolveEffectiveStatus_ReturnsOnline_WhenSignalRConnected()
    {
        var node = new StacksAtlas.Core.Models.FederatedNode
        {
            Id = "mac-intel",
            Status = FederatedNodeStatus.Offline,
            ConnectionId = "live-conn",
            LastSeenUtc = DateTime.UtcNow.AddMinutes(-10),
        };

        var effective = FederatedNodeStatus.ResolveEffectiveStatus(node, signalRConnected: true, DateTime.UtcNow);
        Assert.Equal(FederatedNodeStatus.Online, effective);
    }
}
