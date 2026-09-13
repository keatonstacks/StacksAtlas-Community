using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using StacksAtlas.Core.Models;

namespace StacksAtlas.Core.Services.Network
{
    public class TracerouteService : ITracerouteService
    {
        private const int DnsLookupTimeoutMs = 500;

        public async IAsyncEnumerable<TracerouteHop> RunTracerouteAsync(string targetIp, int maxHops = 30, int timeout = 1000)
        {
            // Validate IP
            if (!IPAddress.TryParse(targetIp, out var address))
            {
                yield break;
            }

            using var ping = new Ping();
            var buffer = Encoding.ASCII.GetBytes("StacksAtlas Traceroute Payload");
            int consecutiveTimeouts = 0;

            for (int ttl = 1; ttl <= maxHops; ttl++)
            {
                var options = new PingOptions(ttl, true);
                var stopwatch = global::System.Diagnostics.Stopwatch.StartNew();
                PingReply? reply = null;

                try
                {
                    reply = await ping.SendPingAsync(address, timeout, buffer, options);
                }
                catch (PingException)
                {
                    // Ignore, treat as timeout/unreachable logic below
                }

                stopwatch.Stop();

                var hop = new TracerouteHop
                {
                    HopNumber = ttl,
                    Status = reply?.Status ?? IPStatus.TimedOut,
                    RoundTripTime = stopwatch.ElapsedMilliseconds
                };

                if (reply != null && (reply.Status == IPStatus.Success || reply.Status == IPStatus.TtlExpired))
                {
                    hop.IpAddress = reply.Address.ToString();
                    hop.Hostname = await ResolveHopHostnameAsync(reply.Address, hop.IpAddress);
                }
                else
                {
                    hop.IpAddress = "*";
                    hop.Hostname = "Request Timed Out";
                }

                if (reply?.Status == IPStatus.Success)
                {
                    hop.IsTarget = true;
                }

                yield return hop;

                // Circuit Breaker: Stop after 5 consecutive timeouts to prevent spam
                if (hop.Status == IPStatus.TimedOut || hop.Status == IPStatus.DestinationHostUnreachable)
                {
                    consecutiveTimeouts++;
                }
                else
                {
                    consecutiveTimeouts = 0;
                }

                if (consecutiveTimeouts >= 5)
                    yield break;

                if (hop.IsTarget)
                    break;
            }
        }

        private static bool IsRfc1918(IPAddress address)
        {
            if (address.AddressFamily != AddressFamily.InterNetwork)
                return false;

            var bytes = address.GetAddressBytes();
            if (bytes.Length != 4)
                return false;

            return bytes[0] == 10
                || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                || (bytes[0] == 192 && bytes[1] == 168);
        }

        private static async Task<string> ResolveHopHostnameAsync(IPAddress address, string ipAddress)
        {
            if (IsRfc1918(address))
                return ipAddress;

            try
            {
                var dnsTask = Dns.GetHostEntryAsync(address);
                var completed = await Task.WhenAny(dnsTask, Task.Delay(DnsLookupTimeoutMs));
                if (completed != dnsTask)
                    return ipAddress;

                return (await dnsTask).HostName;
            }
            catch
            {
                return ipAddress;
            }
        }
    }
}
