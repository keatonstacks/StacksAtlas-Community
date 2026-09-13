using System.Net;
using System.Net.Sockets;

namespace StacksAtlas.Core.Services.Network;

public interface IWakeOnLanService
{
    Task SendMagicPacketAsync(string macAddress);
}

public class WakeOnLanService : IWakeOnLanService
{
    public async Task SendMagicPacketAsync(string macAddress)
    {
        if (string.IsNullOrWhiteSpace(macAddress))
            throw new ArgumentException("MAC address is required", nameof(macAddress));

        // Clean MAC address (remove separators)
        var cleanMac = macAddress
            .Replace(":", "")
            .Replace("-", "")
            .Replace(".", "");

        if (cleanMac.Length != 12)
            throw new ArgumentException("Invalid MAC address format", nameof(macAddress));

        // Parse hex string to byte array
        var macBytes = Convert.FromHexString(cleanMac);

        // Construct Magic Packet: 6x 0xFF followed by 16x MAC Address
        var packet = new byte[6 + 16 * 6];
        
        // Header: 6 bytes of 0xFF
        for (int i = 0; i < 6; i++)
            packet[i] = 0xFF;

        // Payload: 16 copies of the MAC address
        for (int i = 0; i < 16; i++)
            Array.Copy(macBytes, 0, packet, 6 + i * 6, 6);

        // Broadcast the packet
        using var client = new UdpClient();
        client.EnableBroadcast = true;
        
        // Broadcast to port 9 (standard WOL port)
        var targetEp = new IPEndPoint(IPAddress.Broadcast, 9);
        await client.SendAsync(packet, packet.Length, targetEp);
        
        // Also broadcast to port 7 (echo) just in case
        var targetEp7 = new IPEndPoint(IPAddress.Broadcast, 7);
        await client.SendAsync(packet, packet.Length, targetEp7);
    }
}
