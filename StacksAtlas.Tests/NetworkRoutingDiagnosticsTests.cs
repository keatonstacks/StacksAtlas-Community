using Microsoft.Extensions.Logging.Abstractions;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Services.Scanning;
using StacksAtlas.Core.Settings;

namespace StacksAtlas.Tests;

public class NetworkRoutingDiagnosticsTests
{
    [Fact]
    public void Analyze_WindowsMultiNicProfile_DoesNotRequireRemediation()
    {
        if (OperatingSystem.IsLinux())
            return;

        var service = CreateService(
            interfaces:
            [
                CreateInterface("eth0", "10.0.0.5"),
                CreateInterface("eth1", "192.168.1.5")
            ],
            subnets:
            [
                new NetworkScope { Cidr = "10.0.0.0/24", InterfaceId = "eth0-id" },
                new NetworkScope { Cidr = "192.168.1.0/24", InterfaceId = "eth1-id" }
            ],
            rpFilterValues: new Dictionary<string, int> { ["all"] = 1 });

        var result = service.Analyze();

        Assert.True(result.IsMultiHomedProfile);
        Assert.False(result.RpFilterCheckApplicable);
        Assert.False(result.RequiresRemediation);
    }

    [Fact]
    public void Analyze_LinuxMultiNicStrictRpFilter_RequiresRemediation()
    {
        if (!OperatingSystem.IsLinux())
            return;

        var service = CreateService(
            interfaces:
            [
                CreateInterface("eth0", "10.0.0.5", "eth0-id"),
                CreateInterface("eth1", "192.168.1.5", "eth1-id")
            ],
            subnets:
            [
                new NetworkScope { Cidr = "10.0.0.0/24", InterfaceId = "eth0-id" },
                new NetworkScope { Cidr = "192.168.1.0/24", InterfaceId = "eth1-id" }
            ],
            rpFilterValues: new Dictionary<string, int>
            {
                ["all"] = 1,
                ["default"] = 1,
                ["eth0"] = 1,
                ["eth1"] = 1
            });

        var result = service.Analyze();

        Assert.True(result.IsLinux);
        Assert.True(result.IsMultiHomedProfile);
        Assert.True(result.RequiresRemediation);
        Assert.Equal("Strict", result.RpFilterMode);
        Assert.Equal("Warning", result.Severity);
        Assert.Contains("rp_filter=2", result.SysctlCommands[0]);
    }

    [Fact]
    public void Analyze_LinuxMultiNicLooseRpFilter_DoesNotRequireRemediation()
    {
        if (!OperatingSystem.IsLinux())
            return;

        var service = CreateService(
            interfaces:
            [
                CreateInterface("eth0", "10.0.0.5", "eth0-id"),
                CreateInterface("eth1", "192.168.1.5", "eth1-id")
            ],
            subnets:
            [
                new NetworkScope { Cidr = "10.0.0.0/24", InterfaceId = "eth0-id" }
            ],
            rpFilterValues: new Dictionary<string, int> { ["all"] = 2, ["default"] = 2 });

        var result = service.Analyze();

        Assert.True(result.IsMultiHomedProfile);
        Assert.False(result.RequiresRemediation);
        Assert.Equal("Loose", result.RpFilterMode);
    }

    [Fact]
    public void Analyze_SingleInterface_DoesNotRequireRemediationEvenWhenStrict()
    {
        var service = CreateService(
            interfaces: [CreateInterface("eth0", "10.0.0.5", "eth0-id")],
            subnets: [new NetworkScope { Cidr = "10.0.0.0/24", InterfaceId = "eth0-id" }],
            rpFilterValues: new Dictionary<string, int> { ["all"] = 1 });

        var result = service.Analyze();

        Assert.False(result.IsMultiHomedProfile);
        Assert.False(result.RequiresRemediation);
    }

    private static NetworkRoutingDiagnosticsService CreateService(
        List<NetworkInterfaceInfo> interfaces,
        List<NetworkScope> subnets,
        Dictionary<string, int> rpFilterValues)
    {
        return new NetworkRoutingDiagnosticsService(
            new TestNetworkInterfaceService(interfaces),
            new TestNetworkSettingsStore(new NetworkSettings { Subnets = subnets }),
            new TestSysctlReader(rpFilterValues),
            NullLogger<NetworkRoutingDiagnosticsService>.Instance);
    }

    private static NetworkInterfaceInfo CreateInterface(string name, string ip, string? id = null)
    {
        return new NetworkInterfaceInfo(
            id ?? $"{name}-id",
            name,
            "Test Adapter",
            ip,
            "255.255.255.0",
            "10.0.0.1",
            "Ethernet",
            1000,
            "Up");
    }

    private sealed class TestNetworkInterfaceService(List<NetworkInterfaceInfo> interfaces) : INetworkInterfaceService
    {
        public List<NetworkInterfaceInfo> GetAllInterfaces(NetworkInterfaceScope scope = NetworkInterfaceScope.Discovery) => interfaces;
        public NetworkInterfaceInfo? GetTailscaleInterface() => interfaces.FirstOrDefault(i => i.IsFederationTransport);
        public NetworkInterfaceInfo GetActiveInterface() => interfaces[0];
        public void SetActiveInterface(string? id) { }
        public string GetScannerCidr() => "10.0.0.0/24";
    }

    private sealed class TestNetworkSettingsStore(NetworkSettings settings) : INetworkRoutingSettingsProvider
    {
        public NetworkSettings GetSettings() => settings;
    }

    private sealed class TestSysctlReader(Dictionary<string, int> values) : ISysctlReader
    {
        public int? TryReadRpFilter(string interfaceName)
        {
            return values.TryGetValue(interfaceName, out var value) ? value : null;
        }
    }
}
