using System.Net;
using System.Net.Sockets;

namespace StacksAtlas.Core.Helpers;

public static class NetworkHelper
{
    /// <summary>
    /// Returns a display-friendly IP string, stripping IPv6-mapped IPv4 prefixes (e.g. ::ffff:192.168.1.1).
    /// </summary>
    public static string NormalizeIpAddress(string? ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress)) return string.Empty;

        var trimmed = ipAddress.Trim();
        if (trimmed.StartsWith("::ffff:", StringComparison.OrdinalIgnoreCase))
            return trimmed["::ffff:".Length..];

        if (IPAddress.TryParse(trimmed, out var parsed))
        {
            if (parsed.IsIPv4MappedToIPv6)
                return parsed.MapToIPv4().ToString();

            if (parsed.AddressFamily == AddressFamily.InterNetworkV6 && trimmed.Contains('.'))
            {
                var lastColon = trimmed.LastIndexOf(':');
                if (lastColon >= 0 && lastColon < trimmed.Length - 1)
                    return trimmed[(lastColon + 1)..];
            }
        }

        return trimmed;
    }

    public static bool IsSafeNmapScanTarget(string? ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress)) return false;

        var normalized = NormalizeIpAddress(ipAddress);
        if (string.IsNullOrEmpty(normalized)) return false;

        // Reject shell metacharacters and argument injection.
        foreach (var ch in normalized)
        {
            if (char.IsWhiteSpace(ch) || ch is '"' or '\'' or ';' or '|' or '&' or '$' or '`' or '<' or '>')
                return false;
        }

        return IPAddress.TryParse(normalized, out var parsed)
               && (parsed.AddressFamily == AddressFamily.InterNetwork
                   || parsed.AddressFamily == AddressFamily.InterNetworkV6);
    }

    public static bool IsIpInCidr(string ipAddress, string cidr)
    {
        if (string.IsNullOrWhiteSpace(ipAddress) || string.IsNullOrWhiteSpace(cidr)) return false;

        try
        {
            if (!cidr.Contains('/'))
            {
                return ipAddress == cidr;
            }

            var parts = cidr.Split('/');
            var networkAddress = IPAddress.Parse(parts[0]);
            var cidrPrefix = int.Parse(parts[1]);

            var ip = IPAddress.Parse(ipAddress);

            if (networkAddress.AddressFamily != ip.AddressFamily) return false;

            byte[] networkBytes = networkAddress.GetAddressBytes();
            byte[] ipBytes = ip.GetAddressBytes();

            int fullByteCount = cidrPrefix / 8;
            int remainingBitCount = cidrPrefix % 8;

            for (int i = 0; i < fullByteCount; i++)
            {
                if (networkBytes[i] != ipBytes[i]) return false;
            }

            if (remainingBitCount > 0)
            {
                byte mask = (byte)(0xFF << (8 - remainingBitCount));
                if ((networkBytes[fullByteCount] & mask) != (ipBytes[fullByteCount] & mask))
                {
                    return false;
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}
