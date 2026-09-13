using Microsoft.Extensions.Logging.Abstractions;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Services.Devices;

namespace StacksAtlas.Tests;

public class IntelligenceEngineClassificationTests
{
    private readonly IntelligenceEngine _engine = new(NullLogger<IntelligenceEngine>.Instance);

    [Fact]
    public void Synthesize_LgOuiWithQsysPort_DoesNotClassifyAsQsys()
    {
        var hints = new List<ClassificationHint>
        {
            new(DeviceType.Video_Display, "LG Electronics", null, 95, "OUI"),
            new(DeviceType.Audio_DSP, "Q-SYS", "Q-SYS Device", 50, "Port:1702"),
        };

        var (type, model, vendor, _, _) = _engine.Synthesize(hints);

        Assert.Equal(DeviceType.Video_Display, type);
        Assert.Null(model);
        Assert.Equal("LG Electronics", vendor);
    }

    [Fact]
    public void Synthesize_WyrestormOuiWithCrestronPort_KeepsWyreStormVendor()
    {
        var hints = new List<ClassificationHint>
        {
            new(DeviceType.Video_Decoder, "WyreStorm Technologies Ltd", null, 95, "OUI"),
            new(DeviceType.Control_Processor, "Crestron", "Control Processor", 70, "Port:41794"),
        };

        var (type, model, vendor, _, _) = _engine.Synthesize(hints);

        Assert.Equal("WyreStorm Technologies Ltd", vendor);
        Assert.NotEqual(DeviceType.Control_Processor, type);
        Assert.Null(model);
    }

    [Fact]
    public void GetHints_WebOsHostname_ClassifiesAsLgTv()
    {
        var hints = _engine.GetHints("[LG] webOS TV UJ6560", "lg-tv.local", null, null, [1702, 80]);

        var (type, model, vendor, confidence, _) = _engine.Synthesize(hints);

        Assert.Equal(DeviceType.Video_Display, type);
        Assert.Equal("webOS TV", model);
        Assert.Equal("LG", vendor);
        Assert.True(confidence >= 80);
    }

    [Fact]
    public void GetHints_WyrestormHostname_ClassifiesAsWyreStorm()
    {
        var hints = _engine.GetHints("WyreStorm-NHD-500-TX", "wyrestorm-tx.local", null, null, [41794]);

        var (type, model, vendor, _, _) = _engine.Synthesize(hints);

        Assert.Equal(DeviceType.Video_Decoder, type);
        Assert.Equal("WyreStorm", vendor);
        Assert.Equal("WyreStorm Device", model);
    }

    [Fact]
    public void ShouldClearStaleProAvModel_ReturnsTrueForQsysModelOnDisplay()
    {
        Assert.True(IntelligenceEngine.ShouldClearStaleProAvModel("Q-SYS Device", DeviceType.Video_Display));
        Assert.False(IntelligenceEngine.ShouldClearStaleProAvModel("Q-SYS Core", DeviceType.Audio_DSP));
    }

    [Fact]
    public void ShouldAllowTypeCorrection_AllowsOuiDisplayOverStaleAudioDsp()
    {
        var hints = new List<ClassificationHint>
        {
            new(DeviceType.Video_Display, "LG Electronics", null, 95, "OUI"),
            new(DeviceType.Audio_DSP, "Q-SYS", "Q-SYS Device", 50, "Port:1702"),
        };

        Assert.True(IntelligenceEngine.ShouldAllowTypeCorrection(hints, DeviceType.Video_Display, "Audio DSP"));
        Assert.False(IntelligenceEngine.ShouldAllowTypeCorrection(hints, DeviceType.Video_Display, "Video Display"));
    }

    [Theory]
    [InlineData("Audio_DanteDevice", "Audio Dante Device")]
    [InlineData("Collaboration_RoomSystem", "Collaboration Room System")]
    [InlineData("Control_TouchPanel", "Control Touch Panel")]
    [InlineData("VirtualMachine", "Virtual Machine")]
    [InlineData("Audio_AVReceiver", "Audio AV Receiver")]
    [InlineData("Laptop", "Laptop")]
    public void FormatType_SplitsCamelCaseAndUnderscores(string input, string expected)
    {
        Assert.Equal(expected, IntelligenceEngine.FormatType(input));
    }
}
