using StacksAtlas.Core.Helpers;
using Xunit;

namespace StacksAtlas.Tests;

public class VlanTagValidatorTests
{
    [Theory]
    [InlineData("20", "20")]
    [InlineData("AV-VLAN", "AV-VLAN")]
    [InlineData("  100  ", "100")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void TryNormalize_AcceptsValidTags(string? input, string? expected)
    {
        var ok = VlanTagValidator.TryNormalize(input, out var normalized, out var error);
        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("4095")]
    [InlineData("99999")]
    public void TryNormalize_RejectsInvalidNumericVlanIds(string input)
    {
        var ok = VlanTagValidator.TryNormalize(input, out _, out var error);
        Assert.False(ok);
        Assert.Contains("1 and 4094", error);
    }

    [Fact]
    public void TryNormalize_RejectsOverlongLabels()
    {
        var ok = VlanTagValidator.TryNormalize(new string('A', 33), out _, out var error);
        Assert.False(ok);
        Assert.Contains("32", error);
    }
}
