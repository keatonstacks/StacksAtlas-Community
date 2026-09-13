using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using ArpLookup;

namespace StacksAtlas.Core.Models
{
    public static class NetworkUtils
    {
        private static List<string>? _localSubnetsCache;
        private static DateTime _lastCacheUpdate = DateTime.MinValue;
        private static readonly global::System.Threading.Lock _cacheLock = new();

        private static List<(string Ip, string Mac)>? _passiveArpCache;
        private static DateTime _lastArpCacheUpdate = DateTime.MinValue;
        private static readonly global::System.Threading.Lock _arpCacheLock = new();
        private static readonly SemaphoreSlim _arpCacheSemaphore = new(1, 1);

        private static List<string> GetLocalSubnets()
        {
            lock (_cacheLock)
            {
                if (_localSubnetsCache != null && (DateTime.UtcNow - _lastCacheUpdate).TotalMinutes < 5)
                {
                    return _localSubnetsCache;
                }

                var subnets = new List<string>();
                bool success = false;
                try
                {
                    foreach (var ni in global::System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                    {
                        foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                        {
                            if (ua.Address.AddressFamily != global::System.Net.Sockets.AddressFamily.InterNetwork) continue;

                            int prefix = ua.PrefixLength;
                            subnets.Add($"{ua.Address}/{prefix}");
                        }
                    }
                    success = true;
                }
                catch { }

                if (success && subnets.Count > 0)
                {
                    _localSubnetsCache = subnets;
                    _lastCacheUpdate = DateTime.UtcNow;
                }
                
                return _localSubnetsCache ?? new List<string>();
            }
        }

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

        public static bool IsIpLocal(string ipAddress)
        {
            try
            {
                var targetIp = IPAddress.Parse(ipAddress);
                if (IPAddress.IsLoopback(targetIp)) return true;

                var subnets = GetLocalSubnets();
                foreach (var cidr in subnets)
                {
                    if (IsIpInSubnet(ipAddress, cidr)) return true;
                }
            }
            catch { }
            return false;
        }

        public static async Task<List<(string Ip, string Mac)>> GetPassiveNeighborsAsync(CancellationToken token)
        {
            // Fast path cache check
            lock (_arpCacheLock)
            {
                if (_passiveArpCache != null && (DateTime.UtcNow - _lastArpCacheUpdate).TotalSeconds < 10)
                {
                    return _passiveArpCache;
                }
            }

            await _arpCacheSemaphore.WaitAsync(token);
            try
            {
                // Double-checked locking after acquiring semaphore
                lock (_arpCacheLock)
                {
                    if (_passiveArpCache != null && (DateTime.UtcNow - _lastArpCacheUpdate).TotalSeconds < 10)
                    {
                        return _passiveArpCache;
                    }
                }

                var results = new List<(string Ip, string Mac)>();
                try
                {
                    string command = "arp";
                    string args = "-an";

                    using var process = new global::System.Diagnostics.Process();
                    process.StartInfo = new global::System.Diagnostics.ProcessStartInfo
                    {
                        FileName = command,
                        Arguments = args,
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    process.Start();

                    // Prevent orphaned zombie processes if cancelled
                    using var ctr = token.Register(() => 
                    {
                        try { process.Kill(); } catch { }
                    });

                    string output = await process.StandardOutput.ReadToEndAsync(token);
                    await process.WaitForExitAsync(token);

                    var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        var ipMatch = global::System.Text.RegularExpressions.Regex.Match(line, @"(\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})");
                        var macMatch = global::System.Text.RegularExpressions.Regex.Match(line, @"([0-9a-fA-F]{1,2}[:\-][0-9a-fA-F]{1,2}[:\-][0-9a-fA-F]{1,2}[:\-][0-9a-fA-F]{1,2}[:\-][0-9a-fA-F]{1,2}[:\-][0-9a-fA-F]{1,2})");

                        if (ipMatch.Success && macMatch.Success)
                        {
                            var mac = macMatch.Value.Replace(":", "").Replace("-", "").ToUpperInvariant();
                            if (mac != "000000000000" && mac.Length == 12
                                && !StacksAtlas.Core.Services.Scanning.HostPinger.IsLocallyAdministeredMac(mac))
                            {
                                results.Add((ipMatch.Value, mac));
                            }
                        }
                    }

                    lock (_arpCacheLock)
                    {
                        _passiveArpCache = results;
                        _lastArpCacheUpdate = DateTime.UtcNow;
                    }
                    return results;
                }
                catch (OperationCanceledException) { throw; }
                catch { }

                return results;
            }
            finally
            {
                _arpCacheSemaphore.Release();
            }
        }

        public static bool IsIpSelf(string ipAddress)
        {
            try
            {
                if (IPAddress.TryParse(ipAddress, out var target))
                {
                    if (IPAddress.IsLoopback(target)) return true;

                    foreach (var ni in global::System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                    {
                        // OperationalStatus check removed for Docker/Linux compatibility.
                        foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                        {
                            if (ua.Address.Equals(target)) return true;
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        public static async Task<string?> GetMacAddressAsync(string ipAddress, CancellationToken token)
        {
            try
            {
                if (!IPAddress.TryParse(ipAddress, out var address)) return null;

                // PASSIVE LOOKUP: Check the system ARP cache first (netstat/arp)
                // This is fast and works even when active probes are firewalled.
                var passive = await GetPassiveNeighborsAsync(token);
                var cached = passive.FirstOrDefault(p => p.Ip == ipAddress);
                if (!string.IsNullOrEmpty(cached.Mac))
                {
                    return cached.Mac;
                }

                // ACTIVE LOOKUP: Use the cross-platform ArpLookup library
                var physicalAddress = await Arp.LookupAsync(address);
                var activeMac = physicalAddress?.ToString().Replace("-", "").Replace(":", "").ToUpperInvariant();
                
                // Reject locally-administered (synthetic) MACs from active ARP too
                if (!string.IsNullOrEmpty(activeMac) 
                    && !StacksAtlas.Core.Services.Scanning.HostPinger.IsLocallyAdministeredMac(activeMac))
                {
                    return activeMac;
                }
                return null;
            }
            catch (OperationCanceledException) { throw; }
            catch { }

            return null;
        }
    }
}
