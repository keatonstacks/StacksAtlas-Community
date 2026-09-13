using System.Text.RegularExpressions;

namespace StacksAtlas.Core.Services.Updates;

/// <summary>Minimal semver comparison for appliance update gating (major.minor.patch).</summary>
public static partial class SemverUtility
{
    private static readonly Regex VersionPattern = SemverRegex();

    public static bool TryParse(string? input, out VersionParts parts)
    {
        parts = default;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var match = VersionPattern.Match(input.Trim());
        if (!match.Success)
            return false;

        if (!int.TryParse(match.Groups["major"].Value, out var major))
            return false;
        if (!int.TryParse(match.Groups["minor"].Value, out var minor))
            return false;
        if (!int.TryParse(match.Groups["patch"].Value, out var patch))
            return false;

        parts = new VersionParts(major, minor, patch);
        return true;
    }

    public static int Compare(string? left, string? right)
    {
        if (!TryParse(left, out var a))
            throw new ArgumentException($"Invalid semver: {left}", nameof(left));
        if (!TryParse(right, out var b))
            throw new ArgumentException($"Invalid semver: {right}", nameof(right));

        var major = a.Major.CompareTo(b.Major);
        if (major != 0) return major;
        var minor = a.Minor.CompareTo(b.Minor);
        if (minor != 0) return minor;
        return a.Patch.CompareTo(b.Patch);
    }

    /// <summary>Safe compare; returns false when either side is not a parseable major.minor.patch.</summary>
    public static bool TryCompare(string? left, string? right, out int comparison)
    {
        comparison = 0;
        if (!TryParse(left, out var a) || !TryParse(right, out var b))
            return false;

        var major = a.Major.CompareTo(b.Major);
        if (major != 0)
        {
            comparison = major;
            return true;
        }

        var minor = a.Minor.CompareTo(b.Minor);
        if (minor != 0)
        {
            comparison = minor;
            return true;
        }

        comparison = a.Patch.CompareTo(b.Patch);
        return true;
    }

    public static bool IsNewerThan(string? candidate, string? current) =>
        Compare(candidate, current) > 0;

    public readonly record struct VersionParts(int Major, int Minor, int Patch);

    [GeneratedRegex(@"^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex SemverRegex();
}
