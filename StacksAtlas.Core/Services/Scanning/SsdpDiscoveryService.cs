using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace StacksAtlas.Core.Services.Scanning;

public interface ISsdpDiscoveryService
{
    Task<List<string>> DiscoverAsync(string localIp, TimeSpan timeout, CancellationToken token);
}

public sealed class SsdpDiscoveryService(ILogger<SsdpDiscoveryService> logger) : ISsdpDiscoveryService
{
    private readonly ILogger<SsdpDiscoveryService> _logger = logger;
    private static readonly Regex LocationIpRegex = new(@"https?://([^:/]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public async Task<List<string>> DiscoverAsync(string localIp, TimeSpan timeout, CancellationToken token)
    {
        var discoveredIps = new HashSet<string>();
        if (!IPAddress.TryParse(localIp, out var localIpAddress))
        {
            _logger.LogWarning("Invalid local IP address for SSDP: {LocalIp}", localIp);
            return [];
        }

        try
        {
            // Bind to the local IP address on an ephemeral port
            using var client = new UdpClient(new IPEndPoint(localIpAddress, 0));
            
            // EXPLICIT socket option setting for the outgoing interface to isolate multicast traffic
            client.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, localIpAddress.GetAddressBytes());
            
            var request = "M-SEARCH * HTTP/1.1\r\n" +
                          "HOST: 239.255.255.250:1900\r\n" +
                          "MAN: \"ssdp:discover\"\r\n" +
                          "MX: 2\r\n" +
                          "ST: ssdp:all\r\n\r\n";

            var requestBytes = Encoding.UTF8.GetBytes(request);
            var multicastEndPoint = new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900);

            _logger.LogDebug("SSDP M-SEARCH query sent from local IP {LocalIp}", localIp);
            await client.SendAsync(requestBytes, requestBytes.Length, multicastEndPoint);

            var start = DateTime.UtcNow;
            while (DateTime.UtcNow - start < timeout && !token.IsCancellationRequested)
            {
                var remaining = timeout - (DateTime.UtcNow - start);
                if (remaining <= TimeSpan.Zero) break;

                try
                {
                    var receiveTask = client.ReceiveAsync(token).AsTask();
                    var completedTask = await Task.WhenAny(receiveTask, Task.Delay(remaining, token));

                    if (completedTask == receiveTask)
                    {
                        var result = await receiveTask;
                        var responseText = Encoding.UTF8.GetString(result.Buffer);

                        string? deviceIp = null;
                        
                        // Parse LOCATION header
                        var lines = responseText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var line in lines)
                        {
                            if (line.StartsWith("LOCATION:", StringComparison.OrdinalIgnoreCase))
                            {
                                var match = LocationIpRegex.Match(line);
                                if (match.Success)
                                {
                                    deviceIp = match.Groups[1].Value;
                                }
                                break;
                            }
                        }

                        // Fallback to sender's IP if LOCATION parse failed
                        deviceIp ??= result.RemoteEndPoint.Address.ToString();

                        if (IPAddress.TryParse(deviceIp, out var parsedIp))
                        {
                            discoveredIps.Add(parsedIp.ToString());
                        }
                    }
                    else
                    {
                        // Timeout
                        break;
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogTrace(ex, "Error reading SSDP response packet");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SSDP Discovery error on interface IP {LocalIp}", localIp);
        }

        return discoveredIps.ToList();
    }
}
