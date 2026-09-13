using StacksAtlas.Core.Helpers;
using Xunit;

namespace StacksAtlas.Tests;

public class ScanSettingsJsonHelperTests
{
    [Fact]
    public void ReconcileScanSettings_AdoptsIncomingWhenHubPolicyEmpty()
    {
        var incoming = """
        {
          "network": {"subnets":[{"cidr":"192.168.1.0/24"}]},
          "polling": {"refreshIntervalSeconds": 15},
          "interfaces": [{"id":"eth0","name":"eth0","ipAddress":"192.168.1.239"}]
        }
        """;

        var merged = ScanSettingsJsonHelper.ReconcileScanSettings("{}", incoming);

        Assert.Contains("192.168.1.0/24", merged);
        Assert.Contains("refreshIntervalSeconds", merged);
        Assert.Contains("eth0", merged);
    }

    [Fact]
    public void ReconcileScanSettings_KeepsHubPolicyWhenConfigured()
    {
        var hubJson = """{"network":{"subnets":[{"cidr":"10.0.0.0/24"}]},"polling":{"refreshIntervalSeconds":60}}""";
        var incomingJson = """
        {
          "network": {"subnets":[{"cidr":"192.168.1.0/24"}]},
          "polling": {"refreshIntervalSeconds": 15},
          "interfaces": [{"id":"eth0","name":"eth0","ipAddress":"192.168.1.239"}]
        }
        """;

        var merged = ScanSettingsJsonHelper.ReconcileScanSettings(hubJson, incomingJson);

        Assert.Contains("10.0.0.0/24", merged);
        Assert.Contains("eth0", merged);
        Assert.DoesNotContain("192.168.1.0/24", merged);
    }

    [Fact]
    public void MergeInterfaceTelemetry_CopiesInterfacesFromIncoming()
    {
        var baseJson = """{"network":{"subnets":[{"cidr":"10.0.0.0/24"}]},"polling":{"refreshIntervalSeconds":60}}""";
        var incomingJson = """
        {
          "network": {"subnets": []},
          "interfaces": [
            {"id":"eth0","name":"eth0","ipAddress":"192.168.1.10"}
          ]
        }
        """;

        var merged = ScanSettingsJsonHelper.MergeNodeTelemetry(baseJson, incomingJson);

        Assert.Contains("\"interfaces\"", merged);
        Assert.Contains("eth0", merged);
        Assert.Contains("10.0.0.0/24", merged);
    }

    [Fact]
    public void PreserveInterfaceTelemetry_KeepsExistingWhenPatchOmitsInterfaces()
    {
        var existingJson = """
        {
          "network": {"subnets":[{"cidr":"192.168.1.0/24"}]},
          "interfaces":[{"id":"enp0s31f6","name":"enp0s31f6","ipAddress":"192.168.1.239"}]
        }
        """;
        var patchJson = """{"network":{"subnets":[{"cidr":"192.168.1.0/24"}],"interfaceRoleMappings":[{"interfaceId":"enp0s31f6","role":"HubCommunication"}]}}""";

        var merged = ScanSettingsJsonHelper.PreserveNodeTelemetry(existingJson, patchJson);

        Assert.Contains("enp0s31f6", merged);
        Assert.Contains("HubCommunication", merged);
    }
}
