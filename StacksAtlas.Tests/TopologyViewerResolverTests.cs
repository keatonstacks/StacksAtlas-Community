using StacksAtlas.Core.Models;
using StacksAtlas.Core.Services.Network;

namespace StacksAtlas.Tests;

public class TopologyViewerResolverTests
{
    [Fact]
    public void ResolveViewerNodeId_MatchesLanClientByIp()
    {
        var desktopId = Guid.NewGuid();
        var devices = new[]
        {
            Device(desktopId, "192.168.1.50"),
            Device(Guid.NewGuid(), "192.168.1.70"),
        };

        var result = TopologyViewerResolver.ResolveViewerNodeId(
            "192.168.1.50",
            "192.168.1.70",
            devices,
            includeHostMachineNode: true);

        Assert.Equal(desktopId.ToString(), result);
    }

    [Fact]
    public void ResolveViewerNodeId_LoopbackPrefersApplianceInventoryRow()
    {
        var applianceId = Guid.NewGuid();
        var devices = new[] { Device(applianceId, "192.168.1.70") };

        var result = TopologyViewerResolver.ResolveViewerNodeId(
            "127.0.0.1",
            "192.168.1.70",
            devices,
            includeHostMachineNode: true);

        Assert.Equal(applianceId.ToString(), result);
    }

    [Fact]
    public void ResolveViewerNodeId_LoopbackUsesHostMachineWhenApplianceMissingFromInventory()
    {
        var devices = new[] { Device(Guid.NewGuid(), "192.168.1.10") };

        var result = TopologyViewerResolver.ResolveViewerNodeId(
            "::ffff:127.0.0.1",
            "192.168.1.70",
            devices,
            includeHostMachineNode: true);

        Assert.Equal("host_machine", result);
    }

    [Fact]
    public void NormalizeIp_StripsIpv4MappedPrefix()
    {
        Assert.Equal("192.168.1.50", TopologyViewerResolver.NormalizeIp("::ffff:192.168.1.50"));
    }

    private static Device Device(Guid id, string ip) => new()
    {
        Id = id,
        IpAddress = ip,
        Name = ip,
        Status = "online",
    };
}
