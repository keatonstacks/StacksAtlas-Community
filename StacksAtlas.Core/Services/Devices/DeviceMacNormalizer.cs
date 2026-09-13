using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace StacksAtlas.Core.Services.Devices;

public static class DeviceMacNormalizer
{
    private static readonly Regex MacTokenRegex = new(
        @"([0-9a-fA-F]{1,2}[:\-][0-9a-fA-F]{1,2}[:\-][0-9a-fA-F]{1,2}[:\-][0-9a-fA-F]{1,2}[:\-][0-9a-fA-F]{1,2}[:\-][0-9a-fA-F]{1,2})",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Uppercase 12-hex storage form (no separators).</summary>
    public static string Normalize(string? mac)
    {
        if (string.IsNullOrWhiteSpace(mac)) return string.Empty;
        if (TryParse(mac, out var parsed)) return parsed;
        return mac.Replace("-", "").Replace(":", "").Replace(".", "").Replace(" ", "").ToUpperInvariant();
    }

    /// <summary>
    /// Parses MAC strings from arp output. macOS often uses single-digit octets (e.g. 0:11:22:33:44:55)
    /// which naive colon-stripping turns into 11 hex chars and gets rejected.
    /// </summary>
    public static bool TryParse(string? input, out string normalized12)
    {
        normalized12 = string.Empty;
        if (string.IsNullOrWhiteSpace(input)) return false;

        var token = input.Trim();
        var match = MacTokenRegex.Match(token);
        if (match.Success) token = match.Value;

        var parts = token.Split([':', '-'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 6) return false;

        var sb = new StringBuilder(12);
        foreach (var part in parts)
        {
            if (!int.TryParse(part, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var octet) || octet > 255)
                return false;
            sb.Append(octet.ToString("X2", CultureInfo.InvariantCulture));
        }

        normalized12 = sb.ToString();
        return normalized12 != "000000000000";
    }

    public static string SuppressionKey(string nodeId, string normalizedMac) =>
        $"{nodeId}|{normalizedMac}";
}
