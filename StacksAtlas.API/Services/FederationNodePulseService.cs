using StacksAtlas.Core.Helpers;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Services.Federation;
using StacksAtlas.Core.Settings;

namespace StacksAtlas.API.Services;

public sealed record FederationNodePulseResult(bool Success, string? Url, string? Detail);

/// <summary>
/// Hub-initiated HTTP wake-up pulse to a federated Node (exits deep sleep, retries SignalR).
/// </summary>
public sealed class FederationNodePulseService(
    FederationSettingsStore settingsStore,
    ILogger<FederationNodePulseService> logger)
{
    public async Task<FederationNodePulseResult> TryPulseAsync(FederatedNode node, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(node.IPAddress))
            return new FederationNodePulseResult(false, null, "Node has no IP address on file.");

        var token = settingsStore.Current.FederationToken;
        if (string.IsNullOrWhiteSpace(token))
            return new FederationNodePulseResult(false, null, "Hub federation token is not configured.");

        var httpPort = AppliancePortDefaults.ResolveHttpPortForNode(node.HttpPort, node.OS);
        var httpsPort = AppliancePortDefaults.ResolveHttpsPortForNode(node.HttpsPort);
        var urls = new[]
        {
            $"https://{node.IPAddress}:{httpsPort}/api/federation/pulse",
            $"http://{node.IPAddress}:{httpPort}/api/federation/pulse",
        };

        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };

        foreach (var url in urls)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Headers.TryAddWithoutValidation(FederationPulseAuth.HeaderName, token);
                var response = await client.SendAsync(request, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    logger.LogInformation("Federation pulse accepted by Node {NodeId} at {Url}", node.Id, url);
                    return new FederationNodePulseResult(true, url, null);
                }

                logger.LogDebug(
                    "Federation pulse to Node {NodeId} at {Url} returned {Status}",
                    node.Id,
                    url,
                    (int)response.StatusCode);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Federation pulse to Node {NodeId} at {Url} failed", node.Id, url);
            }
        }

        return new FederationNodePulseResult(false, null, "Node did not accept pulse on HTTP or HTTPS.");
    }

    public async Task PulseAllNodesAsync(IReadOnlyList<FederatedNode> nodes, CancellationToken cancellationToken = default)
    {
        if (nodes.Count == 0)
            return;

        var tasks = nodes
            .Where(n => !string.IsNullOrWhiteSpace(n.IPAddress))
            .Select(n => TryPulseAsync(n, cancellationToken));

        await Task.WhenAll(tasks);
    }
}
