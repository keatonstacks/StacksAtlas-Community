using Microsoft.Extensions.Logging.Abstractions;
using StacksAtlas.Core.Abstractions;
using StacksAtlas.Core.Data;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Services.Devices;
using StacksAtlas.Core.Services.Scanning;
using StacksAtlas.Core.Settings;
using StacksAtlas.Core.State;

namespace StacksAtlas.Tests;

public class DeviceAttachmentReconciliationTests : IDisposable
{
    public void Dispose()
    {
        ExecutionState.Initialize(ExecutionMode.Standalone);
    }

    [Fact]
    public void HubTelemetry_IncompleteParentSnapshot_DoesNotWipeHubUplink()
    {
        ExecutionState.Initialize(ExecutionMode.Hub);
        var service = CreateReconciliationService();
        var parentId = Guid.NewGuid();

        var existing = BaseDevice("AABBCCDDEE10");
        existing.AttachmentKind = "Ethernet";
        existing.AttachmentPort = "1/0/12";
        existing.AttachmentParentDeviceId = parentId;
        existing.AttachmentParentMac = "112233445566";
        existing.IsAttachmentManuallySet = true;
        existing.LastModifiedUtc = DateTime.UtcNow.AddMinutes(-1);

        var incoming = BaseDevice(existing.MacAddress!);
        incoming.Id = existing.Id;
        incoming.AttachmentKind = "Ethernet";
        incoming.AttachmentPort = "1/0/12";
        incoming.AttachmentParentDeviceId = null;
        incoming.AttachmentParentMac = null;
        incoming.IsAttachmentManuallySet = true;
        incoming.LastModifiedUtc = DateTime.UtcNow;
        incoming.Status = "online";

        var result = service.ReconcileExisting(existing, incoming, isUserAction: false);

        Assert.Equal(parentId, result.AttachmentParentDeviceId);
        Assert.Equal("112233445566", result.AttachmentParentMac);
        Assert.Equal("1/0/12", result.AttachmentPort);
        Assert.True(result.IsAttachmentManuallySet);
    }

    [Fact]
    public void HubTelemetry_ParentMac_AppliesAndKeepsManualFlag()
    {
        ExecutionState.Initialize(ExecutionMode.Hub);
        var service = CreateReconciliationService();
        var nodeParentId = Guid.NewGuid();

        var existing = BaseDevice("AABBCCDDEE11");
        existing.IsAttachmentManuallySet = false;

        var incoming = BaseDevice(existing.MacAddress!);
        incoming.Id = existing.Id;
        incoming.AttachmentKind = "WiFi";
        incoming.AttachmentPort = "Office";
        incoming.AttachmentParentDeviceId = nodeParentId;
        incoming.AttachmentParentMac = "AABBCCDDEE99";
        incoming.IsAttachmentManuallySet = true;
        incoming.Status = "online";

        var result = service.ReconcileExisting(existing, incoming, isUserAction: false);

        Assert.Equal(nodeParentId, result.AttachmentParentDeviceId);
        Assert.Equal("AABBCCDDEE99", result.AttachmentParentMac);
        Assert.Equal("WiFi", result.AttachmentKind);
        Assert.Equal("Office", result.AttachmentPort);
        Assert.True(result.IsAttachmentManuallySet);
    }

    [Fact]
    public void HubTelemetry_ForeignGuidWithoutMac_DoesNotOverwriteHubUplink()
    {
        ExecutionState.Initialize(ExecutionMode.Hub);
        var service = CreateReconciliationService();
        var hubParentId = Guid.NewGuid();
        var nodeParentId = Guid.NewGuid();

        var existing = BaseDevice("AABBCCDDEE13");
        existing.AttachmentKind = "Ethernet";
        existing.AttachmentPort = "1/0/1";
        existing.AttachmentParentDeviceId = hubParentId;
        existing.AttachmentParentMac = "112233445566";
        existing.IsAttachmentManuallySet = true;

        var incoming = BaseDevice(existing.MacAddress!);
        incoming.Id = existing.Id;
        incoming.AttachmentKind = "Ethernet";
        incoming.AttachmentPort = "1/0/1";
        // Node GUID only  -  no MAC (the "80c8e656" class of bug).
        incoming.AttachmentParentDeviceId = nodeParentId;
        incoming.AttachmentParentMac = null;
        incoming.IsAttachmentManuallySet = true;
        incoming.Status = "online";

        var result = service.ReconcileExisting(existing, incoming, isUserAction: false);

        Assert.Equal(hubParentId, result.AttachmentParentDeviceId);
        Assert.Equal("112233445566", result.AttachmentParentMac);
    }

    [Fact]
    public void HubTelemetry_ParentIpWithoutMac_AppliesUplink()
    {
        ExecutionState.Initialize(ExecutionMode.Hub);
        var service = CreateReconciliationService();
        var nodeParentId = Guid.NewGuid();

        var existing = BaseDevice("AABBCCDDEE14");
        existing.IsAttachmentManuallySet = false;

        var incoming = BaseDevice(existing.MacAddress!);
        incoming.Id = existing.Id;
        incoming.AttachmentKind = "Ethernet";
        incoming.AttachmentPort = "1/0/8";
        incoming.AttachmentParentDeviceId = nodeParentId;
        incoming.AttachmentParentMac = null;
        incoming.AttachmentParentIp = "192.168.1.2";
        incoming.IsAttachmentManuallySet = true;
        incoming.Status = "online";

        var result = service.ReconcileExisting(existing, incoming, isUserAction: false);

        Assert.Equal(nodeParentId, result.AttachmentParentDeviceId);
        Assert.Equal("192.168.1.2", result.AttachmentParentIp);
        Assert.Null(result.AttachmentParentMac);
        Assert.True(result.IsAttachmentManuallySet);
    }

    [Fact]
    public void UserAction_CanClearUplink()
    {
        ExecutionState.Initialize(ExecutionMode.Standalone);
        var service = CreateReconciliationService();

        var existing = BaseDevice("AABBCCDDEE12");
        existing.AttachmentKind = "Ethernet";
        existing.AttachmentPort = "24";
        existing.AttachmentParentDeviceId = Guid.NewGuid();
        existing.AttachmentParentMac = "112233445566";
        existing.IsAttachmentManuallySet = true;

        var incoming = BaseDevice(existing.MacAddress!);
        incoming.Id = existing.Id;
        incoming.AttachmentKind = "Ethernet";
        incoming.AttachmentPort = "24";
        incoming.AttachmentParentDeviceId = null;
        incoming.AttachmentParentMac = null;
        incoming.IsAttachmentManuallySet = true;

        var result = service.ReconcileExisting(existing, incoming, isUserAction: true);

        Assert.Null(result.AttachmentParentDeviceId);
        Assert.Null(result.AttachmentParentMac);
        Assert.True(result.IsAttachmentManuallySet);
    }

    [Fact]
    public void ApplyAttachmentFilters_PortContainsMatch()
    {
        var devices = new List<Device>
        {
            new() { Id = Guid.NewGuid(), AttachmentPort = "1/0/12", AttachmentKind = "Ethernet" },
            new() { Id = Guid.NewGuid(), AttachmentPort = "Gi1/0/24", AttachmentKind = "Ethernet" },
            new() { Id = Guid.NewGuid(), AttachmentPort = "Office-WiFi", AttachmentKind = "WiFi" },
        };

        var matched = DeviceRepository.ApplyAttachmentFilters(devices, null, "2", null);
        Assert.Equal(2, matched.Count);
        Assert.All(matched, d => Assert.Contains("2", d.AttachmentPort!, StringComparison.Ordinal));
    }

    [Fact]
    public void ApplyAttachmentFilters_KindAndParentNone()
    {
        var parentId = Guid.NewGuid();
        var devices = new List<Device>
        {
            new() { Id = Guid.NewGuid(), AttachmentKind = "Fiber", AttachmentParentDeviceId = parentId },
            new() { Id = Guid.NewGuid(), AttachmentKind = "Fiber", AttachmentParentDeviceId = null },
            new() { Id = Guid.NewGuid(), AttachmentKind = "Ethernet", AttachmentParentDeviceId = null },
        };

        var fiberNoUplink = DeviceRepository.ApplyAttachmentFilters(devices, "Fiber", null, "none");
        Assert.Single(fiberNoUplink);
        Assert.Equal("Fiber", fiberNoUplink[0].AttachmentKind);
        Assert.Null(fiberNoUplink[0].AttachmentParentDeviceId);
    }

    private static Device BaseDevice(string mac) => new()
    {
        Id = Guid.NewGuid(),
        IpAddress = "192.168.1.50",
        MacAddress = mac,
        Name = "endpoint",
        NodeId = "site-a",
        Status = "online",
    };

    private static DeviceReconciliationService CreateReconciliationService() =>
        new(
            new RecogMatchingService(NullLogger<RecogMatchingService>.Instance),
            new IntelligenceEngine(NullLogger<IntelligenceEngine>.Instance),
            NullLogger<DeviceReconciliationService>.Instance,
            new TestClock { UtcNow = DateTime.UtcNow },
            new FederationSettingsStore(NullLogger<FederationSettingsStore>.Instance));

    private sealed class TestClock : IClock
    {
        public DateTime UtcNow { get; set; }
        public DateTime Now => UtcNow.ToLocalTime();
    }
}
