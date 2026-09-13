using System.Text.Json;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace StacksAtlas.Tests;

public class InterfaceConfigSyncTests
{
    [Fact]
    public void NetworkSettings_DeserializesStringRolesFromHubJson()
    {
        const string json = """
            {
              "subnets": [{ "cidr": "192.168.1.0/24" }],
              "interfaceConfigs": [{
                "interfaceId": "eth0",
                "roles": ["HubCommunication", "DiscoveryFallback"],
                "defaultVlanTag": "AV-VLAN"
              }],
              "pingTimeoutMs": 200,
              "pingRetries": 0,
              "maxParallelPings": 256,
              "offlineStrikeThreshold": 3,
              "enableInstantRecovery": true,
              "enableNmapDeepScan": false,
              "maxConcurrentDeepScans": 2
            }
            """;

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };

        var settings = JsonSerializer.Deserialize<NetworkSettings>(json, options);

        Assert.NotNull(settings);
        Assert.Single(settings!.InterfaceConfigs);
        Assert.Equal("eth0", settings.InterfaceConfigs[0].InterfaceId);
        Assert.Equal("AV-VLAN", settings.InterfaceConfigs[0].DefaultVlanTag);
        Assert.Contains(NetworkRole.HubCommunication, settings.InterfaceConfigs[0].Roles);
        Assert.Contains(NetworkRole.DiscoveryFallback, settings.InterfaceConfigs[0].Roles);
    }

    [Fact]
    public void ApplyHubGovernancePatch_PreservesExistingSubnetsWhenPatchOmitsThem()
    {
        var store = new NetworkSettingsStore(NullLogger<NetworkSettingsStore>.Instance);
        store.Save(new NetworkSettings
        {
            Subnets = [new NetworkScope { Cidr = "10.0.0.0/24", Id = "scope-a" }],
            InterfaceConfigs = []
        });

        store.ApplyHubGovernancePatch(new NetworkSettings
        {
            InterfaceConfigs =
            [
                new NetworkInterfaceConfig
                {
                    InterfaceId = "eth0",
                    Roles = [NetworkRole.HubCommunication],
                    DefaultVlanTag = "Corp"
                }
            ]
        });

        var current = store.Load();
        Assert.Single(current.Subnets);
        Assert.Equal("10.0.0.0/24", current.Subnets[0].Cidr);
        Assert.Single(current.InterfaceConfigs);
        Assert.Equal("Corp", current.InterfaceConfigs[0].DefaultVlanTag);
    }
}
