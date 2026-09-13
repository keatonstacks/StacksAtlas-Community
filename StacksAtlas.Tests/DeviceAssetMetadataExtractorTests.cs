using StacksAtlas.Core.Services.Devices;

namespace StacksAtlas.Tests;

public class DeviceAssetMetadataExtractorTests
{
    [Fact]
    public void ExtractSerial_ReadsKnownKeys()
    {
        var state = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["serial_number"] = "SN-12345",
        };

        Assert.Equal("SN-12345", DeviceAssetMetadataExtractor.ExtractSerial(state));
    }

    [Fact]
    public void ExtractFirmware_PrefersFirmwareVersionKey()
    {
        var state = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["version"] = "1.0.0",
            ["firmware_version"] = "04.30.41",
        };

        Assert.Equal("04.30.41", DeviceAssetMetadataExtractor.ExtractFirmware(state));
        Assert.Null(DeviceAssetMetadataExtractor.ExtractFirmware(new Dictionary<string, string> { ["version"] = "1.0.0" }));
    }
}
