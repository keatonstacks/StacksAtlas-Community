using StacksAtlas.Core.Models;
using StacksAtlas.Core.Services.Network;

namespace StacksAtlas.Tests;

public class TopologyGatewayResolverTests
{
    [Fact]
    public void ResolveSiteGateway_PrefersRouterType()
    {
        var routerId = Guid.NewGuid();
        var switchId = Guid.NewGuid();
        var router = Device(routerId, "192.168.1.1", "Router", "aa:aa:aa:aa:aa:01");
        var switchDev = Device(switchId, "192.168.1.2", "Switch", "aa:aa:aa:aa:aa:02");
        var clients = new[]
        {
            Device(Guid.NewGuid(), "192.168.1.10", "Workstation", "bb:bb:bb:bb:bb:01", switchId),
            Device(Guid.NewGuid(), "192.168.1.11", "Workstation", "bb:bb:bb:bb:bb:02", switchId),
        };

        var result = TopologyGatewayResolver.ResolveSiteGateway(new[] { switchDev, router }.Concat(clients).ToList());

        Assert.Equal(routerId, result?.Id);
    }

    [Fact]
    public void ResolveSiteGateway_FallsBackToSwitchWithMostChildren()
    {
        var sparseId = Guid.NewGuid();
        var hubId = Guid.NewGuid();
        var sparse = Device(sparseId, "192.168.1.2", "Switch", "aa:aa:aa:aa:aa:02");
        var hub = Device(hubId, "192.168.1.3", "Switch", "aa:aa:aa:aa:aa:03");
        var devices = new List<Device>
        {
            sparse,
            hub,
            Device(Guid.NewGuid(), "192.168.1.10", "Workstation", "bb:bb:bb:bb:bb:01", hubId),
            Device(Guid.NewGuid(), "192.168.1.11", "Workstation", "bb:bb:bb:bb:bb:02", hubId),
            Device(Guid.NewGuid(), "192.168.1.12", "Workstation", "bb:bb:bb:bb:bb:03", hubId),
        };

        var result = TopologyGatewayResolver.ResolveSiteGateway(devices);

        Assert.Equal(hubId, result?.Id);
    }

    [Fact]
    public void ResolveByGatewayIp_MatchesCaseInsensitive()
    {
        var routerId = Guid.NewGuid();
        var router = Device(routerId, "192.168.1.1", "Router", "aa:aa:aa:aa:aa:01");
        var result = TopologyGatewayResolver.ResolveByGatewayIp(new[] { router }, "192.168.1.1");
        Assert.Equal(routerId, result?.Id);
    }

    [Fact]
    public void ResolveSiteGateway_ExcludesDeletedDevices()
    {
        var deletedId = Guid.NewGuid();
        var activeId = Guid.NewGuid();
        var deleted = Device(deletedId, "192.168.1.1", "Router", "aa:aa:aa:aa:aa:01");
        deleted.IsDeleted = true;
        var active = Device(activeId, "192.168.1.2", "Switch", "aa:aa:aa:aa:aa:02");

        var result = TopologyGatewayResolver.ResolveSiteGateway(new[] { deleted, active });

        Assert.Equal(activeId, result?.Id);
    }

    private static Device Device(
        Guid id,
        string ip,
        string type,
        string mac,
        Guid? parentId = null)
    {
        return new Device
        {
            Id = id,
            IpAddress = ip,
            Type = type,
            MacAddress = mac,
            AttachmentParentDeviceId = parentId,
        };
    }
}
