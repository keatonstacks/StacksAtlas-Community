using StacksAtlas.Core.Models;
using StacksAtlas.Core.Services.Devices;

namespace StacksAtlas.Tests;

public class DeviceAssetMetadataPatcherTests
{
    [Fact]
    public void TryApply_ClearsWarrantyWhenEmptyString()
    {
        var device = new Device
        {
            Id = Guid.NewGuid(),
            WarrantyExpiresUtc = new DateTime(2027, 1, 1),
            IsWarrantyExpiresManuallySet = true,
        };

        var error = DeviceAssetMetadataPatcher.TryApply(device, null, null, null, string.Empty);

        Assert.Null(error);
        Assert.Null(device.WarrantyExpiresUtc);
    }

    [Fact]
    public void TryClearManualSerialAndFirmware_LeavesOpenAvcValues()
    {
        var device = new Device
        {
            Id = Guid.NewGuid(),
            SerialNumber = "MANUAL-SN",
            IsSerialNumberManuallySet = true,
            FirmwareVersion = "OAVC-FW",
            IsFirmwareVersionManuallySet = false,
            AssetMetadataSource = AssetMetadataSource.OpenAvc,
        };

        var changed = DeviceAssetMetadataPatcher.TryClearManualSerialAndFirmware(device);

        Assert.True(changed);
        Assert.Null(device.SerialNumber);
        Assert.False(device.IsSerialNumberManuallySet);
        Assert.Equal("OAVC-FW", device.FirmwareVersion);
    }
}
