using StacksAtlas.Core.Helpers;
using Xunit;

namespace StacksAtlas.Tests;

public class NetworkHelperTests
{
    [Theory]
    [InlineData("::ffff:192.168.1.239", "192.168.1.239")]
    [InlineData("192.168.1.239", "192.168.1.239")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void NormalizeIpAddress_StripsIpv6MappedPrefix(string? input, string expected)
    {
        Assert.Equal(expected, NetworkHelper.NormalizeIpAddress(input));
    }

    [Theory]
    [InlineData("192.168.1.10", true)]
    [InlineData("::ffff:10.0.0.1", true)]
    [InlineData("10.0.0.1; rm -rf", false)]
    [InlineData("not-an-ip", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsSafeNmapScanTarget_ValidatesIpOnly(string? input, bool expected)
    {
        Assert.Equal(expected, NetworkHelper.IsSafeNmapScanTarget(input));
    }
}
