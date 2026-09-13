using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace StacksAtlas.Core.Services.Scanning
{
    /// <summary>
    /// Pure utility class for CIDR mathematics and subnet enumeration.
    /// Does not contain OS-dependent state or hardware discovery logic.
    /// </summary>
    public static class NetworkUtils
    {
        public static IEnumerable<string> GetIPsInSubnet(string cidr)
        {
            var parts = cidr.Split('/');
            if (parts.Length != 2) yield break;

            if (!IPAddress.TryParse(parts[0], out var ipAddr)) yield break;
            if (!int.TryParse(parts[1], out int prefix) || prefix < 0 || prefix > 32) yield break;

            var baseAddressBytes = ipAddr.GetAddressBytes();
            Array.Reverse(baseAddressBytes);
            uint baseAddress = BitConverter.ToUInt32(baseAddressBytes, 0);

            uint mask = prefix == 0 ? 0 : uint.MaxValue << (32 - prefix);
            uint network = baseAddress & mask;
            uint broadcast = network + ~mask;

            for (uint ip = network + 1; ip < broadcast; ip++)
            {
                var bytes = BitConverter.GetBytes(ip);
                Array.Reverse(bytes);
                yield return new IPAddress(bytes).ToString();
            }
        }

        public static bool IsIpInSubnet(string ipAddress, string cidr)
        {
            try
            {
                var parts = cidr.Split('/');
                if (parts.Length != 2) return false;

                if (!IPAddress.TryParse(parts[0], out var subnetAddr)) return false;
                if (!IPAddress.TryParse(ipAddress, out var targetAddr)) return false;
                if (!int.TryParse(parts[1], out int prefix)) return false;

                var subnetBytes = subnetAddr.GetAddressBytes();
                var targetBytes = targetAddr.GetAddressBytes();

                if (subnetBytes.Length != targetBytes.Length) return false;

                uint mask = prefix == 0 ? 0 : uint.MaxValue << (32 - prefix);
                uint subnet = BitConverter.ToUInt32(subnetBytes.Reverse().ToArray(), 0) & mask;
                uint target = BitConverter.ToUInt32(targetBytes.Reverse().ToArray(), 0) & mask;

                return subnet == target;
            }
            catch { return false; }
        }

        public static (string Network, int Prefix) ParseCidr(string cidr)
        {
            var parts = cidr.Split('/');
            if (parts.Length == 2 && int.TryParse(parts[1], out int prefix))
            {
                return (parts[0], prefix);
            }
            return (cidr, 32);
        }
    }
}
