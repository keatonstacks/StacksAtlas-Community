using StacksAtlas.Core.Models;
using StacksAtlas.Core.Services.Devices;
using Xunit;

namespace StacksAtlas.Tests;

public class DeviceAttachmentParentCatalogTests
{
    [Theory]
    [InlineData("NetworkRouter", true)]
    [InlineData("Network Router", true)]
    [InlineData("NetworkSwitch", true)]
    [InlineData("Network AP", true)]
    [InlineData("Workstation", false)]
    [InlineData("IoT", false)]
    public void IsInfrastructureType_matches_network_device_types(string type, bool expected)
    {
        Assert.Equal(expected, DeviceAttachmentParentCatalog.IsInfrastructureType(type));
    }

    [Fact]
    public void FilterCandidates_returns_only_infra_devices_for_site()
    {
        var nodeId = "site-a";
        var router = new Device
        {
            Id = Guid.NewGuid(),
            NodeId = nodeId,
            Type = "NetworkRouter",
            Name = "Gateway",
            IpAddress = "192.168.1.1",
        };
        var laptop = new Device
        {
            Id = Guid.NewGuid(),
            NodeId = nodeId,
            Type = "Laptop",
            Name = "Laptop",
            IpAddress = "192.168.1.50",
        };
        var otherSiteSwitch = new Device
        {
            Id = Guid.NewGuid(),
            NodeId = "site-b",
            Type = "NetworkSwitch",
            Name = "Remote Switch",
            IpAddress = "10.0.0.2",
        };

        var results = DeviceAttachmentParentCatalog
            .FilterCandidates([router, laptop, otherSiteSwitch])
            .Where(d => d.NodeId == nodeId)
            .Select(d => d.Name)
            .ToList();

        Assert.Single(results);
        Assert.Equal("Gateway", results[0]);
    }
}
