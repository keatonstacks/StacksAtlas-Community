using System.Text.Json;
using Microsoft.Extensions.Logging;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Services.Scanning;

namespace StacksAtlas.Core.Services.Federation;

public interface ITailscaleStatusService
{
    Task<TailscaleStatus> GetStatusAsync(CancellationToken cancellationToken = default);
}

public sealed class TailscaleStatusService(
    ITailscaleCliRunner cliRunner,
    INetworkInterfaceService interfaceService,
    ILogger<TailscaleStatusService> logger) : ITailscaleStatusService
{
    private readonly ITailscaleCliRunner _cliRunner = cliRunner;
    private readonly INetworkInterfaceService _interfaceService = interfaceService;
    private readonly ILogger<TailscaleStatusService> _logger = logger;

    public async Task<TailscaleStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var status = new TailscaleStatus();

        if (_cliRunner.IsCliAvailable())
        {
            status.CliInstalled = true;
            var cliStatus = await TryReadFromCliAsync(cancellationToken);
            if (cliStatus != null)
                return cliStatus;
        }

        return InferFromNetworkInterface(status);
    }

    private async Task<TailscaleStatus?> TryReadFromCliAsync(CancellationToken cancellationToken)
    {
        var status = new TailscaleStatus { CliInstalled = true };

        var (jsonSuccess, jsonOutput, jsonError) = await _cliRunner.RunAsync("status --json", cancellationToken);
        if (!jsonSuccess || string.IsNullOrWhiteSpace(jsonOutput))
        {
            _logger.LogDebug("Tailscale CLI status unavailable: {Error}", jsonError);
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(jsonOutput);
            var root = doc.RootElement;

            if (root.TryGetProperty("BackendState", out var backendState))
                status.BackendState = backendState.GetString();

            if (root.TryGetProperty("Self", out var self))
            {
                if (self.TryGetProperty("Online", out var onlineProp))
                    status.Connected = onlineProp.GetBoolean();

                if (self.TryGetProperty("HostName", out var hostProp))
                    status.Hostname = hostProp.GetString();

                if (self.TryGetProperty("DNSName", out var dnsProp))
                {
                    var dns = dnsProp.GetString();
                    status.MagicDnsName = string.IsNullOrWhiteSpace(dns) ? null : dns.Trim().TrimEnd('.');
                }

                if (self.TryGetProperty("TailscaleIPs", out var ipsProp) && ipsProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var ipElement in ipsProp.EnumerateArray())
                    {
                        var ip = ipElement.GetString();
                        if (!string.IsNullOrWhiteSpace(ip) && ip.StartsWith("100.", StringComparison.Ordinal))
                        {
                            status.TailnetIpv4 = ip;
                            break;
                        }
                    }
                }

                if (self.TryGetProperty("KeyExpiry", out var expiryProp))
                {
                    var expiryRaw = expiryProp.GetString();
                    if (!string.IsNullOrWhiteSpace(expiryRaw) &&
                        DateTime.TryParse(expiryRaw, null, System.Globalization.DateTimeStyles.RoundtripKind, out var expiryUtc))
                    {
                        status.KeyExpiryUtc = expiryUtc.ToUniversalTime();
                        status.KeyExpired = expiryUtc.ToUniversalTime() <= DateTime.UtcNow;
                        status.KeyExpiringSoon = !status.KeyExpired &&
                            expiryUtc.ToUniversalTime() <= DateTime.UtcNow.AddDays(14);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse Tailscale status JSON.");
            return null;
        }

        if (string.IsNullOrWhiteSpace(status.TailnetIpv4))
        {
            var (ipSuccess, ipOutput, _) = await _cliRunner.RunAsync("ip -4", cancellationToken);
            if (ipSuccess && !string.IsNullOrWhiteSpace(ipOutput))
                status.TailnetIpv4 = ipOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        }

        if (status.BackendState?.Contains("Expired", StringComparison.OrdinalIgnoreCase) == true)
            status.KeyExpired = true;

        return status;
    }

    private TailscaleStatus InferFromNetworkInterface(TailscaleStatus status)
    {
        var tailscale = _interfaceService.GetTailscaleInterface();
        if (tailscale == null)
        {
            status.ErrorMessage = status.CliInstalled
                ? "Tailscale CLI is installed but status is unavailable, and no tailscale0 interface was detected."
                : "Tailscale not detected. Install Tailscale on this host, ensure tailscaled is running, and use Docker host networking (or mount tailscaled.sock) so tailscale0 is visible.";
            return status;
        }

        status.DetectedViaInterface = true;
        status.Connected = tailscale.Status.Equals("Up", StringComparison.OrdinalIgnoreCase);
        status.TailnetIpv4 = tailscale.IpAddress;
        status.Hostname = tailscale.Name;
        status.ErrorMessage = status.CliInstalled
            ? null
            : "Tailscale inferred from tailscale0 network interface (CLI not available in this runtime  -  typical for Docker host-network deployments).";

        _logger.LogDebug(
            "Tailscale status inferred from interface {InterfaceName} ({IpAddress})",
            tailscale.Name,
            tailscale.IpAddress);

        return status;
    }
}
