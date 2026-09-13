using StacksAtlas.Core.Models;
using StacksAtlas.Core.Services.Scanning;

namespace StacksAtlas.Core.Services.Network;

/// <summary>
/// A No-Op implementation of network services for Hub mode.
/// </summary>
public class NoOpNetworkInterfaceService : INetworkInterfaceService
{
    public List<NetworkInterfaceInfo> GetAllInterfaces(NetworkInterfaceScope scope = NetworkInterfaceScope.Discovery) => new();

    public NetworkInterfaceInfo? GetTailscaleInterface() => null;

    public NetworkInterfaceInfo GetActiveInterface() => new(
        "hub_mode",
        "Hub Mode",
        "Local NIC discovery disabled in Hub mode.",
        "N/A",
        "255.255.255.255",
        null,
        "Virtual",
        0,
        "Up"
    );

    public void SetActiveInterface(string? id) { }

    public string GetScannerCidr() => "N/A";
}

public class NoOpNetworkService : INetworkService
{
    public string GetLocalIpAddress() => "127.0.0.1";
    public string GetLocalMacAddress() => string.Empty;
    public bool IsIpSelf(string ip) => false;
    public bool IsIpLocal(string ip) => false;
    public bool IsIpInSubnet(string ipAddress, string cidr) => false;
    public Task<List<(string Ip, string Mac)>> GetPassiveNeighborsAsync(CancellationToken token) => Task.FromResult(new List<(string Ip, string Mac)>());
    public Task<string?> GetMacAddressAsync(string ipAddress, CancellationToken token, bool activeOnly = false) => Task.FromResult<string?>(null);
    public Task<bool> ProbeArpAsync(string ipAddress, CancellationToken token) => Task.FromResult(false);
    public string GetScannerCidr() => "N/A";
}
