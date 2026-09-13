using StacksAtlas.Core.Models;
using StacksAtlas.Core.Settings;

namespace StacksAtlas.Core.Services.Federation;

public sealed record HubEndpointCandidate(string Label, string Host, int Port, string Path);

public sealed class HubEndpointPlan
{
    public required List<HubEndpointCandidate> Candidates { get; init; }
    public string? SniHostName { get; init; }
    public bool UsesTailscaleTransport { get; init; }

    public string BuildUrl(HubEndpointCandidate candidate) =>
        $"https://{candidate.Host}:{candidate.Port}{candidate.Path}";
}

public sealed class HubEndpointResolver(FederationSettingsStore settingsStore)
{
    private readonly FederationSettingsStore _settingsStore = settingsStore;

    public HubEndpointPlan Resolve(bool useMtls, string path = "/api/federation/realtime") =>
        ResolveFromSettings(_settingsStore.Current, useMtls, path);

    public static HubEndpointPlan ResolveFromSettings(
        FederationSettings settings,
        bool useMtls,
        string path = "/api/federation/realtime")
    {
        var port = useMtls ? 5002 : 5001;
        var normalizedPath = string.IsNullOrWhiteSpace(path) ? "/api/federation/realtime" : path;

        if (!settings.UseTailscaleForHubConnection)
            return ResolveLegacy(settings.HubUrl, port, normalizedPath);

        var magicDns = NormalizeHost(settings.HubTailscaleMagicDns);
        var tailnetIp = NormalizeHost(settings.HubTailscaleIpv4);

        if (string.IsNullOrWhiteSpace(magicDns) && string.IsNullOrWhiteSpace(tailnetIp))
            return ResolveLegacy(settings.HubUrl, port, normalizedPath);

        var candidates = new List<HubEndpointCandidate>();
        // Prefer tailnet IP first: MagicDNS often fails in containers without tailnet DNS in resolv.conf.
        if (!string.IsNullOrWhiteSpace(tailnetIp))
            candidates.Add(new HubEndpointCandidate("TailnetIPv4", tailnetIp, port, normalizedPath));

        if (!string.IsNullOrWhiteSpace(magicDns) &&
            !string.Equals(tailnetIp, magicDns, StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add(new HubEndpointCandidate("MagicDNS", magicDns, port, normalizedPath));
        }

        if (candidates.Count == 0)
            return ResolveLegacy(settings.HubUrl, port, normalizedPath);

        return new HubEndpointPlan
        {
            Candidates = candidates,
            SniHostName = magicDns ?? tailnetIp,
            UsesTailscaleTransport = true
        };
    }

    public HubEndpointPlan ResolveBootstrap(string path = "/api/federation/enroll") =>
        Resolve(useMtls: false, path: path);

    private static HubEndpointPlan ResolveLegacy(string? hubUrl, int port, string path)
    {
        if (string.IsNullOrWhiteSpace(hubUrl))
        {
            return new HubEndpointPlan
            {
                Candidates = [],
                UsesTailscaleTransport = false
            };
        }

        var uri = new Uri(hubUrl.TrimEnd('/'));
        var host = uri.Host;
        // HubUrl stores bootstrap/UI URL (typically :5001). Enrolled mTLS SignalR always uses :5002.
        var resolvedPort = port == 5002 ? 5002 : (uri.Port > 0 ? uri.Port : port);

        return new HubEndpointPlan
        {
            Candidates = [new HubEndpointCandidate("ConfiguredHubUrl", host, resolvedPort, path)],
            SniHostName = host,
            UsesTailscaleTransport = false
        };
    }

    private static string? NormalizeHost(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().TrimEnd('.');
}
