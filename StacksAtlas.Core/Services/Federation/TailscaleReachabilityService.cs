using System.Diagnostics;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Settings;

namespace StacksAtlas.Core.Services.Federation;

public interface ITailscaleReachabilityService
{
    Task<TailscaleReachabilityResult> TestHubReachabilityAsync(
        TailscaleReachabilityProbeRequest? draft = null,
        CancellationToken cancellationToken = default);
}

public sealed class TailscaleReachabilityService(
    FederationSettingsStore settingsStore,
    ILogger<TailscaleReachabilityService> logger) : ITailscaleReachabilityService
{
    private readonly FederationSettingsStore _settingsStore = settingsStore;
    private readonly ILogger<TailscaleReachabilityService> _logger = logger;

    public async Task<TailscaleReachabilityResult> TestHubReachabilityAsync(
        TailscaleReachabilityProbeRequest? draft = null,
        CancellationToken cancellationToken = default)
    {
        var settings = ApplyDraft(_settingsStore.Current, draft);
        if (!settings.UseTailscaleForHubConnection &&
            string.IsNullOrWhiteSpace(settings.HubTailscaleMagicDns) &&
            string.IsNullOrWhiteSpace(settings.HubTailscaleIpv4))
        {
            return new TailscaleReachabilityResult
            {
                Summary = "Configure Hub tailnet identity or enable Tailscale transport before testing reachability.",
                Probes = []
            };
        }

        var plan = HubEndpointResolver.ResolveFromSettings(settings, useMtls: true);
        var probes = new List<TailscaleReachabilityProbe>();

        foreach (var candidate in plan.Candidates)
        {
            foreach (var port in new[] { 5001, 5002 })
            {
                probes.Add(await ProbeAsync($"{candidate.Label}:{port}", candidate.Host, port, cancellationToken));
            }
        }

        if (plan.Candidates.Count == 0)
        {
            return new TailscaleReachabilityResult
            {
                Summary = "No Hub endpoint candidates resolved for the supplied Tailscale settings.",
                Probes = probes
            };
        }

        var reachable = probes.Where(p => p.Reachable).ToList();
        var summary = reachable.Count == 0
            ? "Hub is not reachable over Tailscale on ports 5001 or 5002."
            : $"Hub reachable via {string.Join(", ", reachable.Select(p => p.Label))}.";

        return new TailscaleReachabilityResult
        {
            AnyReachable = reachable.Count > 0,
            Summary = summary,
            Probes = probes
        };
    }

    public static FederationSettings ApplyDraft(
        FederationSettings saved,
        TailscaleReachabilityProbeRequest? draft)
    {
        if (draft == null)
            return saved;

        var json = System.Text.Json.JsonSerializer.Serialize(saved);
        var merged = System.Text.Json.JsonSerializer.Deserialize<FederationSettings>(json)!;

        if (draft.UseTailscaleForHubConnection.HasValue)
            merged.UseTailscaleForHubConnection = draft.UseTailscaleForHubConnection.Value;
        if (draft.HubTailscaleMagicDns != null)
            merged.HubTailscaleMagicDns = draft.HubTailscaleMagicDns;
        if (draft.HubTailscaleIpv4 != null)
            merged.HubTailscaleIpv4 = draft.HubTailscaleIpv4;

        return merged;
    }

    private async Task<TailscaleReachabilityProbe> ProbeAsync(string label, string host, int port, CancellationToken cancellationToken)
    {
        var probe = new TailscaleReachabilityProbe
        {
            Label = label,
            Host = host,
            Port = port
        };

        var sw = Stopwatch.StartNew();
        try
        {
            using var client = new TcpClient();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
            await client.ConnectAsync(host, port, timeoutCts.Token);
            sw.Stop();
            probe.Reachable = true;
            probe.LatencyMs = (int)sw.ElapsedMilliseconds;
        }
        catch (Exception ex)
        {
            sw.Stop();
            probe.Reachable = false;
            probe.Error = ex.Message;
            _logger.LogDebug(ex, "Tailscale reachability probe failed for {Host}:{Port}", host, port);
        }

        return probe;
    }
}
