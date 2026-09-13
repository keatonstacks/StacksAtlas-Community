using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Settings;
using Microsoft.Extensions.Logging;

namespace StacksAtlas.Core.Services.Scanning;

public interface INetworkBindingService
{
    IPAddress? GetIpForRole(NetworkRole role);
    SocketsHttpHandler CreateHandlerForRole(NetworkRole role);
}

public sealed class NetworkBindingService(
    NetworkSettingsStore settingsStore,
    INetworkInterfaceService interfaceService,
    FederationSettingsStore federationSettingsStore,
    ILogger<NetworkBindingService> logger) : INetworkBindingService
{
    private readonly NetworkSettingsStore _settingsStore = settingsStore;
    private readonly INetworkInterfaceService _interfaceService = interfaceService;
    private readonly FederationSettingsStore _federationSettingsStore = federationSettingsStore;
    private readonly ILogger<NetworkBindingService> _logger = logger;

    public IPAddress? GetIpForRole(NetworkRole role)
    {
        if (role == NetworkRole.HubCommunication &&
            _federationSettingsStore.Current.UseTailscaleForHubConnection)
        {
            var tailscale = _interfaceService.GetTailscaleInterface();
            if (tailscale != null && IPAddress.TryParse(tailscale.IpAddress, out var tailscaleIp))
            {
                _logger.LogDebug(
                    "Using Tailscale interface {InterfaceName} ({Ip}) for HubCommunication binding (Tailscale transport enabled)",
                    tailscale.Name,
                    tailscale.IpAddress);
                return tailscaleIp;
            }
        }

        var settings = _settingsStore.Load();
        var mapping = settings.InterfaceRoleMappings?.FirstOrDefault(m => m.Role == role);
        if (mapping != null && !string.IsNullOrWhiteSpace(mapping.InterfaceId))
        {
            var configured = ResolveInterfaceIp(mapping.InterfaceId, NetworkInterfaceScope.Policy);
            if (configured != null)
                return configured;
        }

        _logger.LogDebug("No interface mapping configured for network role: {Role}", role);
        return null;
    }

    public SocketsHttpHandler CreateHandlerForRole(NetworkRole role)
    {
        var handler = new SocketsHttpHandler();
        var localIp = GetIpForRole(role);
        if (localIp != null)
        {
            _logger.LogDebug("Creating SocketsHttpHandler for role {Role} bound to local IP {LocalIp}", role, localIp);
            handler.ConnectCallback = async (context, token) =>
            {
                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                socket.Bind(new IPEndPoint(localIp, 0));
                try
                {
                    await socket.ConnectAsync(context.DnsEndPoint, token).ConfigureAwait(false);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch (Exception ex)
                {
                    if (role == NetworkRole.HubCommunication)
                    {
                        _logger.LogDebug(
                            ex,
                            "HubCommunication outbound connect via {LocalIp} failed (Hub may be offline or unreachable)",
                            localIp);
                    }
                    else
                    {
                        _logger.LogError(ex, "Failed to connect socket bound to local IP {LocalIp} for role {Role}", localIp, role);
                    }
                    socket.Dispose();
                    throw;
                }
            };
        }
        else
        {
            _logger.LogDebug("No local IP resolved for role {Role}, using default SocketsHttpHandler connection routing", role);
        }
        return handler;
    }

    private IPAddress? ResolveInterfaceIp(string interfaceId, NetworkInterfaceScope scope)
    {
        var match = _interfaceService.GetAllInterfaces(scope).FirstOrDefault(i => i.Id == interfaceId);
        if (match == null)
        {
            _logger.LogWarning("Configured interface ID {InterfaceId} was not found among active interfaces.", interfaceId);
            return null;
        }

        if (IPAddress.TryParse(match.IpAddress, out var ip))
        {
            _logger.LogDebug("Resolved IP {Ip} on interface {InterfaceName}", ip, match.Name);
            return ip;
        }

        return null;
    }
}
