using StacksAtlas.Core.Helpers;
using Xunit;

namespace StacksAtlas.Tests;

public class AppliancePortDefaultsTests
{
    [Theory]
    [InlineData("Darwin 24.0.0", 5000, 5050)]
    [InlineData("macOS 14.5", 5000, 5050)]
    [InlineData("Ubuntu 22.04", 5000, 5000)]
    [InlineData("Darwin 24.0.0", 5050, 5050)]
    [InlineData(null, 5000, 5000)]
    public void ResolveHttpPortForNode_NormalizesMacLegacyPort(string? os, int input, int expected)
    {
        Assert.Equal(expected, AppliancePortDefaults.ResolveHttpPortForNode(input, os));
    }

    [Theory]
    [InlineData("Darwin", true)]
    [InlineData("Microsoft Windows 11", false)]
    [InlineData("", false)]
    public void IsMacOsDescription_DetectsMac(string os, bool expected)
    {
        Assert.Equal(expected, AppliancePortDefaults.IsMacOsDescription(os));
    }
}
