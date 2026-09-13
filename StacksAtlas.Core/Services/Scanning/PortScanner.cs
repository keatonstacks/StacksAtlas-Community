using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace StacksAtlas.Core.Services.Scanning;

public static class PortScanner
{
    private const int DEFAULT_TIMEOUT_MS = 150;
    private const int MAX_CONCURRENCY = 32;

    public static readonly int[] ScannedPorts =
    [
        21, 22, 23, 25, 80, 443, 445, 631, 3389,                // Standard IT (FTP, SSH, Telnet, SMTP, HTTP, SMB, RDP)
        389, 5000, 5001, 5222, 8008, 8009, 8080, 8081, 8443,    // Web/Media/Chat (Synology, Chromecast, Alt-HTTP, LDAP, XMPP)
        2869, 5353, 62078,                                      // Discovery (UPnP, mDNS, Apple)
        554, 8000, 8899, 37777,                                 // Cameras (RTSP, ONVIF/Hikvision, Dahua)
        5959, 5960, 5961,                                       // NDI
        41794, 41795, 41796, 41797,                             // Crestron
        1700, 1702, 1710, 1704,                                 // Q-SYS
        1317, 2202,                                             // Biamp/Shure Wireless
        1319, 5900, 32400,                                      // AMX/VNC/Plex
        44400,                                                  // Dante Control
        8087, 10001,                                            // Sennheiser, Ubiquiti/GlobalCache
        111, 3260                                               // NFS, iSCSI
    ];

    public static async Task<List<int>> ScanInterestingPortsAsync(string ip, CancellationToken token = default)
    {
        if (!IPAddress.TryParse(ip, out var address)) return [];
        
        var openPorts = new ConcurrentBag<int>();
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = MAX_CONCURRENCY,
            CancellationToken = token
        };

        try
        {
            await Parallel.ForEachAsync(ScannedPorts, options, async (port, ct) =>
            {
                if (await CheckPortAsync(address, port, DEFAULT_TIMEOUT_MS, ct))
                {
                    openPorts.Add(port);
                }
            });
        }
        catch (OperationCanceledException)
        {
            throw;
        }

        return [.. openPorts.OrderBy(p => p)];
    }

    private static async Task<bool> CheckPortAsync(IPAddress address, int port, int timeout, CancellationToken token)
    {
        using var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        socket.Blocking = false; // Ensure non-blocking for better async behavior

        try
        {
            // Optimization: Use WaitAsync on the ConnectAsync task to handle timeout 
            // without allocating a new CancellationTokenSource for every port.
            var connectTask = socket.ConnectAsync(new IPEndPoint(address, port), token).AsTask();
            
            if (await Task.WhenAny(connectTask, Task.Delay(timeout, token)) == connectTask)
            {
                await connectTask; // Propagate any socket exceptions
                return socket.Connected;
            }
            
            return false; // Timeout
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (SocketException)
        {
            return false;
        }
        catch
        {
            return false;
        }
    }
}
