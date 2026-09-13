using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using StacksAtlas.Core.Models;

namespace StacksAtlas.Core.Services.Scanning;

public interface INetworkInterfaceService
{
    List<NetworkInterfaceInfo> GetAllInterfaces(NetworkInterfaceScope scope = NetworkInterfaceScope.Discovery);
    NetworkInterfaceInfo? GetTailscaleInterface();
    NetworkInterfaceInfo GetActiveInterface();
    void SetActiveInterface(string? id);
    string GetScannerCidr();
}

public class NetworkInterfaceService(ILogger<NetworkInterfaceService> logger) : INetworkInterfaceService
{
    private readonly ILogger<NetworkInterfaceService> _logger = logger;
    private string? _forcedInterfaceId;
    private string? _lastDetectedId;

    public List<NetworkInterfaceInfo> GetAllInterfaces(NetworkInterfaceScope scope = NetworkInterfaceScope.Discovery)
    {
        var interfaces = new List<NetworkInterfaceInfo>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            var isTailscale = IsTailscaleAdapter(nic);

            // 1. Hardware/Type Filter
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel && !isTailscale) continue;
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Unknown && !isTailscale) continue;
            if (scope == NetworkInterfaceScope.Discovery && (nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel || isTailscale)) continue;

            // 2. Connectivity Filter (Must be Up and Enabled)
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            
            // 3. Virtualization/Pseudo Filter (Heuristic)  -  Tailscale is allowed in Policy scope
            if (!isTailscale)
            {
                if (nic.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase)) continue;
                if (nic.Description.Contains("Pseudo", StringComparison.OrdinalIgnoreCase)) continue;
                if (nic.Description.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase)) continue;
                if (nic.Description.Contains("WSL", StringComparison.OrdinalIgnoreCase)) continue;
                if (nic.Description.Contains("VMware", StringComparison.OrdinalIgnoreCase)) continue;
                if (nic.Description.Contains("VirtualBox", StringComparison.OrdinalIgnoreCase)) continue;
            }

            var props = nic.GetIPProperties();
            var ipv4 = props.UnicastAddresses
                .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);

            var ipAddress = ipv4?.Address.ToString() ?? "0.0.0.0";
            
            // 4. IP Validity Filter (Must have a real identity)
            if (ipAddress == "0.0.0.0") continue;

            var gateway = props.GatewayAddresses
                .FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString();

            interfaces.Add(new NetworkInterfaceInfo(
                nic.Id,
                nic.Name,
                nic.Description,
                ipAddress,
                ipv4?.IPv4Mask?.ToString() ?? "0.0.0.0",
                gateway,
                nic.NetworkInterfaceType.ToString(),
                nic.Speed,
                nic.OperationalStatus.ToString(),
                isTailscale
            ));
        }
        return interfaces;
    }

    public NetworkInterfaceInfo? GetTailscaleInterface() =>
        GetAllInterfaces(NetworkInterfaceScope.Policy).FirstOrDefault(i => i.IsFederationTransport);

    private static bool IsTailscaleAdapter(NetworkInterface nic) =>
        nic.Name.Equals("tailscale0", StringComparison.OrdinalIgnoreCase) ||
        nic.Description.Contains("Tailscale", StringComparison.OrdinalIgnoreCase);

    public virtual NetworkInterfaceInfo GetActiveInterface()
    {
        var all = GetAllInterfaces().Where(i => i.Status == "Up").ToList();
        
        // 1. Check if we have a forced ID
        if (!string.IsNullOrEmpty(_forcedInterfaceId))
        {
            var forced = all.FirstOrDefault(i => i.Id == _forcedInterfaceId);
            if (forced != null) return forced;
        }

        // 2. Filter for Routable Interfaces
        var routable = all.Where(i => !i.IpAddress.StartsWith("169.254.") && !i.IpAddress.StartsWith("127.")).ToList();

        // 3. Auto-detect logic via Socket
        NetworkInterfaceInfo? match = null;
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
            socket.Connect("8.8.8.8", 65530);
            if (socket.LocalEndPoint is IPEndPoint endPoint)
            {
                var ip = endPoint.Address.ToString();
                match = routable.FirstOrDefault(i => i.IpAddress == ip);
            }
        }
        catch { }

        if (match != null) 
        {
            if (_lastDetectedId != match.Id)
            {
                _logger.LogDebug("Active Interface Changed: {Name} ({Ip}) via Socket", match.Name, match.IpAddress);
                _lastDetectedId = match.Id;
            }
            return match;
        }

        // 4. Fallback: Find first routable interface with physical priority
        var bestCandidate = routable
            .OrderByDescending(i => i.Type == "Ethernet" || i.Type == "Wireless80211")
            .ThenByDescending(i => !i.Description.Contains("Virtual"))
            .FirstOrDefault();

        if (bestCandidate != null)
        {
            if (_lastDetectedId != bestCandidate.Id)
            {
                _logger.LogDebug("Active Interface Changed: {Name} ({Ip}) via Fallback", bestCandidate.Name, bestCandidate.IpAddress);
                _lastDetectedId = bestCandidate.Id;
            }
            return bestCandidate;
        }

        // 5. Final Resort: Return anything we have, even if it's Link-Local
        var fallback = all.FirstOrDefault() ?? new NetworkInterfaceInfo("loopback", "Loopback", "Localhost", "127.0.0.1", "255.0.0.0", null, "Internal", 0, "Up");
        if (_lastDetectedId != fallback.Id)
        {
            _logger.LogDebug("Active Interface Changed: {Name} ({Ip}) via Final Resort", fallback.Name, fallback.IpAddress);
            _lastDetectedId = fallback.Id;
        }
        return fallback;
    }

    public void SetActiveInterface(string? id)
    {
        _forcedInterfaceId = id;
        _lastDetectedId = null; // Reset detection to force a log on next get
        _logger.LogInformation("Active network interface set to: {Id}", id ?? "Auto-detect");
    }

    public virtual string GetScannerCidr()
    {
        var active = GetActiveInterface();
        if (active.IpAddress == "127.0.0.1") return "127.0.0.1/32";

        var ip = IPAddress.Parse(active.IpAddress);
        var mask = IPAddress.Parse(active.SubnetMask);
        
        // Calculate CIDR prefix
        int prefix = 0;
        byte[] maskBytes = mask.GetAddressBytes();
        foreach (byte b in maskBytes)
        {
            byte temp = b;
            while (temp > 0)
            {
                if ((temp & 0x80) == 0x80) prefix++;
                temp <<= 1;
            }
        }

        // Calculate Network Address
        byte[] ipBytes = ip.GetAddressBytes();
        byte[] networkBytes = new byte[ipBytes.Length];
        for (int i = 0; i < ipBytes.Length; i++)
        {
            networkBytes[i] = (byte)(ipBytes[i] & maskBytes[i]);
        }
        var networkIp = new IPAddress(networkBytes);

        return $"{networkIp}/{prefix}";
    }
}

public record NetworkInterfaceInfo(
    string Id, 
    string Name, 
    string Description, 
    string IpAddress, 
    string SubnetMask, 
    string? GatewayAddress,
    string Type, 
    long Speed,
    string Status,
    bool IsFederationTransport = false)
{
    public string GetCidr()
    {
        if (IpAddress == "127.0.0.1") return "127.0.0.1/32";
        if (string.IsNullOrEmpty(SubnetMask) || SubnetMask == "0.0.0.0") return $"{IpAddress}/32";

        try
        {
            var ip = IPAddress.Parse(IpAddress);
            var mask = IPAddress.Parse(SubnetMask);
            
            int prefix = 0;
            byte[] maskBytes = mask.GetAddressBytes();
            foreach (byte b in maskBytes)
            {
                byte temp = b;
                while (temp > 0)
                {
                    if ((temp & 0x80) == 0x80) prefix++;
                    temp <<= 1;
                }
            }

            byte[] ipBytes = ip.GetAddressBytes();
            byte[] networkBytes = new byte[ipBytes.Length];
            for (int i = 0; i < ipBytes.Length; i++)
            {
                networkBytes[i] = (byte)(ipBytes[i] & maskBytes[i]);
            }
            var networkIp = new IPAddress(networkBytes);
            return $"{networkIp}/{prefix}";
        }
        catch { return $"{IpAddress}/32"; }
    }
}
