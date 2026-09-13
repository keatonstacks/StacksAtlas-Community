using StacksAtlas.Core.Services.Devices;
using Xunit;

namespace StacksAtlas.Tests;

public class DeviceMacNormalizerTests
{
    [Theory]
    [InlineData("0:11:22:33:44:55", "001122334455")]
    [InlineData("f0:18:98:aa:bb:cc", "F01898AABBCC")]
    [InlineData("F0-18-98-AA-BB-CC", "F01898AABBCC")]
    [InlineData("? (192.168.1.50) at 0:11:22:33:44:55 on en0", "001122334455")]
    public void TryParse_normalizes_macOS_single_digit_octets(string input, string expected)
    {
        Assert.True(DeviceMacNormalizer.TryParse(input, out var mac));
        Assert.Equal(expected, mac);
    }

    [Fact]
    public void TryParse_rejects_incomplete_zero_mac()
    {
        Assert.False(DeviceMacNormalizer.TryParse("00:00:00:00:00:00", out _));
    }
}
