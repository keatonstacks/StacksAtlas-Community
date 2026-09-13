using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using StacksAtlas.Core.Abstractions;
using StacksAtlas.Core.Data;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Services.Devices;
using StacksAtlas.Core.Services.Reports;
using StacksAtlas.Core.Services.Scanning;
using StacksAtlas.Core.Settings;
using StacksAtlas.Core.State;

namespace StacksAtlas.Tests;

public class DiscoveryProvenanceTests
{
    [Fact]
    public void NetworkSettingsValidate_AssignsStableScopeIds()
    {
        var store = new NetworkSettingsStore(NullLogger<NetworkSettingsStore>.Instance);
        var settings = new NetworkSettings
        {
            Subnets =
            [
                new NetworkScope { Cidr = "192.168.1.0/24" },
                new NetworkScope { Id = "scope-fixed", Cidr = "10.0.0.0/24" }
            ]
        };

        store.Save(settings);

        Assert.False(string.IsNullOrWhiteSpace(store.Current.Subnets[0].Id));
        Assert.Equal("scope-fixed", store.Current.Subnets[1].Id);
    }

    [Fact]
    public void ReconcileExisting_PreservesDiscoveryProvenanceOnceSet()
    {
        var service = CreateReconciliationService();
        var existing = new Device
        {
            IpAddress = "192.168.1.50",
            DiscoveryScopeId = "scope-a",
            DiscoveryInterfaceId = "eth0-id",
            DiscoveryInterfaceName = "eth0"
        };
        var incoming = new Device
        {
            IpAddress = "192.168.1.50",
            DiscoveryScopeId = "scope-b",
            DiscoveryInterfaceId = "eth1-id",
            DiscoveryInterfaceName = "eth1"
        };

        var result = service.ReconcileExisting(existing, incoming, isUserAction: false);

        Assert.Equal("scope-a", result.DiscoveryScopeId);
        Assert.Equal("eth0-id", result.DiscoveryInterfaceId);
        Assert.Equal("eth0", result.DiscoveryInterfaceName);
    }

    [Fact]
    public void PrepareNewDevice_RetainsIncomingDiscoveryProvenance()
    {
        var service = CreateReconciliationService();
        ExecutionState.Initialize(ExecutionMode.Standalone);

        var incoming = new Device
        {
            IpAddress = "192.168.20.10",
            MacAddress = "AABBCCDDEEFF",
            DiscoveryScopeId = "scope-av",
            DiscoveryInterfaceId = "eth1-id",
            DiscoveryInterfaceName = "eth1"
        };

        var prepared = service.PrepareNewDevice(incoming);

        Assert.Equal("scope-av", prepared.DiscoveryScopeId);
        Assert.Equal("eth1-id", prepared.DiscoveryInterfaceId);
        Assert.Equal("eth1", prepared.DiscoveryInterfaceName);
    }

    private static DeviceReconciliationService CreateReconciliationService()
    {
        return new DeviceReconciliationService(
            new RecogMatchingService(NullLogger<RecogMatchingService>.Instance),
            new IntelligenceEngine(NullLogger<IntelligenceEngine>.Instance),
            NullLogger<DeviceReconciliationService>.Instance,
            new TestClock { UtcNow = DateTime.UtcNow },
            new FederationSettingsStore(NullLogger<FederationSettingsStore>.Instance));
    }

    private sealed class TestClock : IClock
    {
        public DateTime UtcNow { get; set; }
        public DateTime Now => UtcNow.ToLocalTime();
    }
}
