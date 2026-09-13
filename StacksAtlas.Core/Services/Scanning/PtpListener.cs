namespace StacksAtlas.Core.Services.Scanning;

public class PtpPacket
{
    public string SourceIp { get; set; } = string.Empty;
    public byte MessageType { get; set; }
    public byte Version { get; set; }
    public byte DomainNumber { get; set; }
    public int Priority1 { get; set; }
    public int Priority2 { get; set; } // Added Priority 2
    public int ClockClass { get; set; } // Added Clock Class (AES67 uses 248, Dante 248/250)
    public int StepsRemoved { get; set; }
    public string ClockIdentity { get; set; } = string.Empty;
    public string FriendlyVendor { get; set; } = "Unknown";
    public bool IsDante => ClockIdentity.Contains(":FF:FE:");
}

public static class PtpListener
{
    private const int PTP_GENERAL_PORT = 320;
    private const string PTP_MULTICAST_GROUP = "224.0.1.129";

    /// <summary>
    /// Listens for PTP Announce packets for a short duration to identify the Grandmaster.
    /// </summary>
    /// <param name="localIp">The local IP to bind to. Crucial for multi-homed NICs.</param>
    public static async global::System.Threading.Tasks.Task<PtpPacket?> DetectGrandmasterAsync(string? localIp = null, int timeoutMs = 2000)
    {
        using var client = new global::System.Net.Sockets.UdpClient();
        try
        {
            var localAddr = string.IsNullOrEmpty(localIp) ? global::System.Net.IPAddress.Any : global::System.Net.IPAddress.Parse(localIp);
            
            client.Client.SetSocketOption(global::System.Net.Sockets.SocketOptionLevel.Socket, global::System.Net.Sockets.SocketOptionName.ReuseAddress, true);
            client.Client.Bind(new global::System.Net.IPEndPoint(localAddr, PTP_GENERAL_PORT));
            
            if (localAddr != global::System.Net.IPAddress.Any)
            {
                client.JoinMulticastGroup(global::System.Net.IPAddress.Parse(PTP_MULTICAST_GROUP), localAddr);
            }
            else
            {
                client.JoinMulticastGroup(global::System.Net.IPAddress.Parse(PTP_MULTICAST_GROUP));
            }

            using var cts = new global::System.Threading.CancellationTokenSource(timeoutMs);
            
            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    var result = await client.ReceiveAsync(cts.Token);
                    var data = result.Buffer;

                    if (data.Length < 34) continue;

                    byte messageType = (byte)(data[0] & 0x0F);
                    byte version = (byte)(data[1] & 0x0F);
                    
                    if (messageType == 0xB || messageType == 0xC) // Announce or Signaling
                    {
                        if (data.Length < 64) continue;

                        var identity = global::System.BitConverter.ToString(data, 53, 8).Replace("-", ":");
                        var vendor = "PTP Device";

                        // Fingerprint: Dante (Audinate OUI 00:1D:C1 + FF:FE spacer)
                        if (identity.StartsWith("00:1D:C1") && identity.Contains(":FF:FE:"))
                        {
                            vendor = "Dante (Audinate)";
                        }
                        // Fingerprint: AES67 / PTPv2 Standard Clock Classes
                        else if (data[48] == 248) 
                        {
                            vendor = "AES67/PTPv2 Clock";
                        }

                        return new PtpPacket
                        {
                            SourceIp = result.RemoteEndPoint.Address.ToString(),
                            MessageType = messageType,
                            Version = version,
                            DomainNumber = data[4],
                            Priority1 = data[47],
                            ClockClass = data[48],
                            Priority2 = data[52],
                            ClockIdentity = identity,
                            StepsRemoved = (data[61] << 8) | data[62],
                            FriendlyVendor = vendor
                        };
                    }
                }
                catch (global::System.OperationCanceledException)
                {
                    break;
                }
            }
        }
        catch (global::System.Exception ex)
        {
            global::System.Diagnostics.Debug.WriteLine($"PTP Listen Error: {ex.Message}");
        }

        return null;
    }
}
