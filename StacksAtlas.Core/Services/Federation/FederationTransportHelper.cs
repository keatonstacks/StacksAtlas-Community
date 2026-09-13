using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Services.Scanning;
using StacksAtlas.Core.Settings;

namespace StacksAtlas.Core.Services.Federation;

public sealed class FederationTransportHelper(
    INetworkBindingService bindingService,
    FederationSettingsStore settingsStore,
    INetworkInterfaceService interfaceService,
    ILogger<FederationTransportHelper> logger)
{
    private readonly INetworkBindingService _bindingService = bindingService;
    private readonly FederationSettingsStore _settingsStore = settingsStore;
    private readonly INetworkInterfaceService _interfaceService = interfaceService;
    private readonly ILogger<FederationTransportHelper> _logger = logger;

    public SocketsHttpHandler CreateHandler(
        HubEndpointPlan plan,
        HubEndpointCandidate candidate,
        X509Certificate2? clientCert,
        X509Certificate2? hubRootCert)
    {
        var handler = _bindingService.CreateHandlerForRole(NetworkRole.HubCommunication);
        ConfigureTailscaleBinding(handler, plan);

        if (!string.IsNullOrWhiteSpace(plan.SniHostName))
            handler.SslOptions.TargetHost = plan.SniHostName;

        if (clientCert != null && hubRootCert != null)
        {
            handler.SslOptions.ClientCertificates = new X509Certificate2Collection(clientCert);
            handler.SslOptions.RemoteCertificateValidationCallback = (_, certificate, _, _) =>
            {
                if (certificate == null)
                    return false;

                using var chainInstance = new X509Chain();
                chainInstance.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                chainInstance.ChainPolicy.CustomTrustStore.Add(hubRootCert);
                chainInstance.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
                chainInstance.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;

                return chainInstance.Build((X509Certificate2)certificate);
            };
        }
        else if (_settingsStore.Current.AllowUntrustedHubs)
        {
            handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        }

        _logger.LogDebug(
            "Prepared federation transport handler for {Label} ({Host}:{Port}) with SNI {SniHost}",
            candidate.Label,
            candidate.Host,
            candidate.Port,
            plan.SniHostName ?? candidate.Host);

        return handler;
    }

    public HttpClient CreateHttpClient(
        HubEndpointPlan plan,
        HubEndpointCandidate candidate,
        X509Certificate2? clientCert,
        X509Certificate2? hubRootCert)
    {
        var handler = CreateHandler(plan, candidate, clientCert, hubRootCert);
        handler.PooledConnectionLifetime = TimeSpan.FromMinutes(2);
        return new HttpClient(handler, disposeHandler: true);
    }

    private void ConfigureTailscaleBinding(SocketsHttpHandler handler, HubEndpointPlan plan)
    {
        if (!plan.UsesTailscaleTransport)
            return;

        var existingIp = _bindingService.GetIpForRole(NetworkRole.HubCommunication);
        if (existingIp != null)
            return;

        var tailscale = _interfaceService.GetTailscaleInterface();
        if (tailscale == null || !System.Net.IPAddress.TryParse(tailscale.IpAddress, out var bindIp))
            return;

        _logger.LogInformation("Auto-binding federation transport to Tailscale interface {InterfaceName} ({Ip})", tailscale.Name, tailscale.IpAddress);
        handler.ConnectCallback = async (context, token) =>
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.Bind(new System.Net.IPEndPoint(bindIp, 0));
            try
            {
                await socket.ConnectAsync(context.DnsEndPoint, token).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to connect via Tailscale bind IP {BindIp}", bindIp);
                socket.Dispose();
                throw;
            }
        };
    }
}
