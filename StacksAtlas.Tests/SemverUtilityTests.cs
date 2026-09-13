using StacksAtlas.Core.Services.Updates;
using Xunit;

namespace StacksAtlas.Tests;

public sealed class SemverUtilityTests
{
    [Theory]
    [InlineData("1.0.0", "1.0.1", -1)]
    [InlineData("1.6.0", "1.5.9", 1)]
    [InlineData("2.0.0", "1.99.99", 1)]
    [InlineData("1.5.0", "1.5.0", 0)]
    public void Compare_OrdersMajorMinorPatch(string left, string right, int expected)
    {
        Assert.Equal(expected, SemverUtility.Compare(left, right));
    }

    [Theory]
    [InlineData("1.6.1", "1.6.0", true)]
    [InlineData("1.6.0", "1.6.0", false)]
    [InlineData("1.5.9", "1.6.0", false)]
    public void IsNewerThan_DetectsUpgradeAvailability(string candidate, string current, bool expected)
    {
        Assert.Equal(expected, SemverUtility.IsNewerThan(candidate, current));
    }

    [Theory]
    [InlineData("1.6.0", true)]
    [InlineData("v1.6.0", false)]
    [InlineData("1.6", false)]
    [InlineData("", false)]
    public void TryParse_ValidatesStrictSemver(string input, bool expected)
    {
        Assert.Equal(expected, SemverUtility.TryParse(input, out _));
    }
}
