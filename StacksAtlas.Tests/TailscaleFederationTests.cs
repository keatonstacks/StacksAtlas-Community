using Microsoft.Extensions.Logging.Abstractions;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Services.Federation;
using StacksAtlas.Core.Services.Scanning;
using StacksAtlas.Core.Settings;
using Xunit;

namespace StacksAtlas.Tests;

public class HubEndpointResolverTests
{
    [Fact]
    public void Resolve_UsesMagicDnsAndTailnetFallback_WhenTailscaleEnabled()
    {
        var store = CreateStore(new FederationSettings
        {
            UseTailscaleForHubConnection = true,
            HubTailscaleMagicDns = "hub.example.ts.net",
            HubTailscaleIpv4 = "100.64.0.5"
        });

        var resolver = new HubEndpointResolver(store);
        var plan = resolver.Resolve(useMtls: true);

        Assert.True(plan.UsesTailscaleTransport);
        Assert.Equal("hub.example.ts.net", plan.SniHostName);
        Assert.Equal(2, plan.Candidates.Count);
        Assert.Equal("TailnetIPv4", plan.Candidates[0].Label);
        Assert.Equal("MagicDNS", plan.Candidates[1].Label);
        Assert.Equal(5002, plan.Candidates[0].Port);
    }

    [Fact]
    public void Resolve_FallsBackToHubUrl_WhenTailscaleDisabled()
    {
        var store = CreateStore(new FederationSettings
        {
            HubUrl = "https://192.168.1.10:5001",
            UseTailscaleForHubConnection = false
        });

        var resolver = new HubEndpointResolver(store);
        var plan = resolver.Resolve(useMtls: true);

        Assert.False(plan.UsesTailscaleTransport);
        Assert.Single(plan.Candidates);
        Assert.Equal("192.168.1.10", plan.Candidates[0].Host);
        Assert.Equal(5002, plan.Candidates[0].Port);
        Assert.Equal("https://192.168.1.10:5002/api/federation/realtime", plan.BuildUrl(plan.Candidates[0]));
    }

    [Fact]
    public void Resolve_UsesHubUrlPort_WhenLegacyTokenAuth()
    {
        var store = CreateStore(new FederationSettings
        {
            HubUrl = "https://192.168.1.10:5001",
            UseTailscaleForHubConnection = false
        });

        var resolver = new HubEndpointResolver(store);
        var plan = resolver.Resolve(useMtls: false);

        Assert.Single(plan.Candidates);
        Assert.Equal(5001, plan.Candidates[0].Port);
    }

    private static FederationSettingsStore CreateStore(FederationSettings settings)
    {
        StacksAtlas.Core.Helpers.PlatformPaths.EnsureDirectoriesExist();
        var store = new FederationSettingsStore(NullLogger<FederationSettingsStore>.Instance);
        store.Save(settings);
        return store;
    }
}

public class TailscaleReachabilityServiceTests
{
    [Fact]
    public void ApplyDraft_OverridesTailscaleFields_WithoutPersisting()
    {
        var saved = new FederationSettings
        {
            UseTailscaleForHubConnection = false,
            HubTailscaleMagicDns = "saved.example.ts.net",
            HubTailscaleIpv4 = "100.64.0.1"
        };

        var merged = TailscaleReachabilityService.ApplyDraft(saved, new TailscaleReachabilityProbeRequest
        {
            UseTailscaleForHubConnection = true,
            HubTailscaleMagicDns = "draft.example.ts.net",
            HubTailscaleIpv4 = "100.64.0.99"
        });

        Assert.True(merged.UseTailscaleForHubConnection);
        Assert.Equal("draft.example.ts.net", merged.HubTailscaleMagicDns);
        Assert.Equal("100.64.0.99", merged.HubTailscaleIpv4);

        Assert.False(saved.UseTailscaleForHubConnection);
        Assert.Equal("100.64.0.1", saved.HubTailscaleIpv4);
    }

    [Fact]
    public void ResolveFromSettings_UsesDraftTailnetIdentity_WhenTailscaleEnabled()
    {
        var plan = HubEndpointResolver.ResolveFromSettings(new FederationSettings
        {
            UseTailscaleForHubConnection = true,
            HubTailscaleIpv4 = "100.74.74.30",
            HubTailscaleMagicDns = "hub.example.ts.net"
        }, useMtls: true);

        Assert.True(plan.UsesTailscaleTransport);
        Assert.Equal("100.74.74.30", plan.Candidates[0].Host);
    }
}

public class TailscaleHubSyncConfiguratorTests
{
    [Fact]
    public void ApplyToNetworkSettings_MovesHubSyncRoleToTailscaleAdapter()
    {
        var settings = new NetworkSettings
        {
            InterfaceConfigs =
            [
                new NetworkInterfaceConfig
                {
                    InterfaceId = "eth0-id",
                    Roles = [NetworkRole.HubCommunication, NetworkRole.DiscoveryFallback]
                }
            ]
        };

        var interfaces = new FakeNetworkInterfaceService(
            new NetworkInterfaceInfo("ts-id", "tailscale0", "Tailscale", "100.126.209.33", "255.255.255.255", null, "Tunnel", 0, "Up", true));

        var changed = TailscaleHubSyncConfigurator.ApplyToNetworkSettings(
            settings,
            interfaces,
            NullLogger.Instance);

        Assert.True(changed);
        var eth0 = settings.InterfaceConfigs.Single(c => c.InterfaceId == "eth0-id");
        Assert.DoesNotContain(NetworkRole.HubCommunication, eth0.Roles);
        Assert.Contains(NetworkRole.DiscoveryFallback, eth0.Roles);

        var tailscale = settings.InterfaceConfigs.Single(c => c.InterfaceId == "ts-id");
        Assert.Contains(NetworkRole.HubCommunication, tailscale.Roles);
    }

    [Fact]
    public void ApplyToNetworkSettings_ReturnsFalse_WhenTailscaleMissing()
    {
        var settings = new NetworkSettings();
        var interfaces = new FakeNetworkInterfaceService();

        var changed = TailscaleHubSyncConfigurator.ApplyToNetworkSettings(
            settings,
            interfaces,
            NullLogger.Instance);

        Assert.False(changed);
    }
}

public class NetworkBindingServiceTests
{
    [Fact]
    public void GetIpForRole_PrefersTailscale_WhenTailscaleTransportEnabled()
    {
        var networkStore = new NetworkSettingsStore(NullLogger<NetworkSettingsStore>.Instance);
        networkStore.Save(new NetworkSettings
        {
            InterfaceConfigs =
            [
                new NetworkInterfaceConfig
                {
                    InterfaceId = "eth0-id",
                    Roles = [NetworkRole.HubCommunication]
                }
            ]
        });

        var federationStore = new FederationSettingsStore(NullLogger<FederationSettingsStore>.Instance);
        federationStore.Save(new FederationSettings { UseTailscaleForHubConnection = true });

        var interfaces = new FakeNetworkInterfaceService(
            new NetworkInterfaceInfo("eth0-id", "eth0", "Ethernet", "192.168.1.239", "255.255.255.0", "192.168.1.1", "Ethernet", 0, "Up", false),
            new NetworkInterfaceInfo("ts-id", "tailscale0", "Tailscale", "100.126.209.33", "255.255.255.255", null, "Tunnel", 0, "Up", true));

        var service = new NetworkBindingService(
            networkStore,
            interfaces,
            federationStore,
            NullLogger<NetworkBindingService>.Instance);

        var ip = service.GetIpForRole(NetworkRole.HubCommunication);

        Assert.NotNull(ip);
        Assert.Equal("100.126.209.33", ip.ToString());
    }
}

internal sealed class FakeNetworkInterfaceService(params NetworkInterfaceInfo[] interfaces) : INetworkInterfaceService
{
    private readonly List<NetworkInterfaceInfo> _interfaces = interfaces.ToList();

    public List<NetworkInterfaceInfo> GetAllInterfaces(NetworkInterfaceScope scope = NetworkInterfaceScope.Discovery) => _interfaces;
    public NetworkInterfaceInfo? GetTailscaleInterface() => _interfaces.FirstOrDefault(i => i.IsFederationTransport);
    public NetworkInterfaceInfo GetActiveInterface() => _interfaces.Count > 0 ? _interfaces[0] : throw new InvalidOperationException("No interfaces");
    public void SetActiveInterface(string? id) { }
    public string GetScannerCidr() => "10.0.0.0/24";
}

public class TailscaleStatusServiceTests
{
    [Fact]
    public async Task GetStatusAsync_ParsesSelfNodeFromJson()
    {
        const string json = """
            {
              "BackendState": "Running",
              "Self": {
                "Online": true,
                "HostName": "node-linux",
                "DNSName": "node-linux.example.ts.net.",
                "TailscaleIPs": ["100.64.0.12"],
                "KeyExpiry": "2026-12-31T00:00:00Z"
              }
            }
            """;

        var runner = new FakeTailscaleCliRunner(json, "100.64.0.12");
        var interfaces = new FakeNetworkInterfaceService();
        var service = new TailscaleStatusService(runner, interfaces, NullLogger<TailscaleStatusService>.Instance);

        var status = await service.GetStatusAsync();

        Assert.True(status.CliInstalled);
        Assert.True(status.Connected);
        Assert.Equal("node-linux.example.ts.net", status.MagicDnsName);
        Assert.Equal("100.64.0.12", status.TailnetIpv4);
        Assert.Equal("Running", status.BackendState);
    }

    private sealed class FakeTailscaleCliRunner(string statusJson, string ipv4) : ITailscaleCliRunner
    {
        public bool IsCliAvailable() => true;

        public Task<(bool Success, string Output, string? Error)> RunAsync(string arguments, CancellationToken cancellationToken = default)
        {
            if (arguments.Contains("status --json", StringComparison.Ordinal))
                return Task.FromResult((true, statusJson, (string?)null));

            if (arguments.Contains("ip -4", StringComparison.Ordinal))
                return Task.FromResult((true, ipv4, (string?)null));

            return Task.FromResult((false, string.Empty, (string?)"unsupported command"));
        }
    }

    [Fact]
    public async Task GetStatusAsync_InfersFromTailscaleInterface_WhenCliMissing()
    {
        var runner = new MissingCliRunner();
        var interfaces = new FakeNetworkInterfaceService(
            new NetworkInterfaceInfo("ts0", "tailscale0", "Tailscale", "100.126.209.33", "255.255.255.255", null, "Tunnel", 0, "Up", true));
        var service = new TailscaleStatusService(runner, interfaces, NullLogger<TailscaleStatusService>.Instance);

        var status = await service.GetStatusAsync();

        Assert.True(status.DetectedViaInterface);
        Assert.True(status.Connected);
        Assert.Equal("100.126.209.33", status.TailnetIpv4);
    }

    private sealed class MissingCliRunner : ITailscaleCliRunner
    {
        public bool IsCliAvailable() => false;
        public Task<(bool Success, string Output, string? Error)> RunAsync(string arguments, CancellationToken cancellationToken = default) =>
            Task.FromResult((false, string.Empty, (string?)null));
    }
}
