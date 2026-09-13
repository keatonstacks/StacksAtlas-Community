namespace StacksAtlas.Core.Models;

/// <summary>
/// Optional draft Tailscale transport values for reachability dry-run probes (unsaved UI form state).
/// </summary>
public sealed class TailscaleReachabilityProbeRequest
{
    public bool? UseTailscaleForHubConnection { get; set; }
    public string? HubTailscaleMagicDns { get; set; }
    public string? HubTailscaleIpv4 { get; set; }
}
