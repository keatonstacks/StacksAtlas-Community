using Microsoft.Extensions.Logging.Abstractions;
using StacksAtlas.Core.Abstractions;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Services.Devices;
using StacksAtlas.Core.Services.Scanning;
using StacksAtlas.Core.Settings;
using StacksAtlas.Core.State;

namespace StacksAtlas.Tests;

public class DeviceAssetReconciliationTests
{
    [Fact]
    public void ReconcileExisting_UserAction_PersistsAssetMetadata()
    {
        ExecutionState.Initialize(ExecutionMode.Standalone);
        var service = CreateReconciliationService();

        var existing = new Device
        {
            Id = Guid.NewGuid(),
            IpAddress = "192.168.1.239",
            MacAddress = "AABBCCDDEEFF",
            Name = "dell7k",
        };

        var incoming = new Device
        {
            Id = existing.Id,
            IpAddress = existing.IpAddress,
            MacAddress = existing.MacAddress,
            Name = existing.Name,
            SerialNumber = "SN-TEST",
            AssetTag = "RACK-7",
            FirmwareVersion = "2.4.1",
            WarrantyExpiresUtc = new DateTime(2028, 1, 15),
            IsSerialNumberManuallySet = true,
            IsAssetTagManuallySet = true,
            IsFirmwareVersionManuallySet = true,
            IsWarrantyExpiresManuallySet = true,
            AssetMetadataSource = AssetMetadataSource.Manual,
        };

        var result = service.ReconcileExisting(existing, incoming, isUserAction: true);

        Assert.Equal("SN-TEST", result.SerialNumber);
        Assert.Equal("RACK-7", result.AssetTag);
        Assert.Equal("2.4.1", result.FirmwareVersion);
        Assert.Equal(new DateTime(2028, 1, 15), result.WarrantyExpiresUtc);
        Assert.True(result.IsAssetTagManuallySet);
    }

    [Fact]
    public void ReconcileExisting_ManualHostname_NotOverwrittenByDiscovery()
    {
        ExecutionState.Initialize(ExecutionMode.Standalone);
        var service = CreateReconciliationService();

        var existing = new Device
        {
            Id = Guid.NewGuid(),
            IpAddress = "192.168.1.50",
            MacAddress = "AABBCCDDEE01",
            Name = "printer-east",
            Hostname = "printer-east.local",
            IsHostnameManuallySet = true,
        };

        var incoming = new Device
        {
            Id = existing.Id,
            IpAddress = existing.IpAddress,
            MacAddress = existing.MacAddress,
            Name = existing.Name,
            Hostname = "auto-discovered-name",
            Status = "online",
        };

        var result = service.ReconcileExisting(existing, incoming, isUserAction: false);

        Assert.Equal("printer-east.local", result.Hostname);
        Assert.True(result.IsHostnameManuallySet);
    }

    [Fact]
    public void ReconcileExisting_UserAction_CanSetHostname()
    {
        ExecutionState.Initialize(ExecutionMode.Standalone);
        var service = CreateReconciliationService();

        var existing = new Device
        {
            Id = Guid.NewGuid(),
            IpAddress = "192.168.1.51",
            MacAddress = "AABBCCDDEE02",
            Name = "192.168.1.51",
        };

        var incoming = new Device
        {
            Id = existing.Id,
            IpAddress = existing.IpAddress,
            MacAddress = existing.MacAddress,
            Name = existing.Name,
            Hostname = "av-rack-01.local",
            IsHostnameManuallySet = true,
        };

        var result = service.ReconcileExisting(existing, incoming, isUserAction: true);

        Assert.Equal("av-rack-01.local", result.Hostname);
        Assert.True(result.IsHostnameManuallySet);
    }

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
