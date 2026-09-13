using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace StacksAtlas.Core.Services.Scanning;

public interface INetworkService
{
    string GetLocalIpAddress();
    string GetLocalMacAddress();
    bool IsIpSelf(string ip);
    bool IsIpLocal(string ip);
    bool IsIpInSubnet(string ipAddress, string cidr);
    Task<List<(string Ip, string Mac)>> GetPassiveNeighborsAsync(CancellationToken token);
    /// <param name="activeOnly">When true, skip passive ARP cache (avoids stale entries after disconnect).</param>
    Task<string?> GetMacAddressAsync(string ipAddress, CancellationToken token, bool activeOnly = false);
    Task<bool> ProbeArpAsync(string ipAddress, CancellationToken token);
    string GetScannerCidr();
}
