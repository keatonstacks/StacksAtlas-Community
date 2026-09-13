namespace StacksAtlas.Core.Models;

public class TailscaleStatus
{
    public bool CliInstalled { get; set; }
    public bool Connected { get; set; }
    public string? Hostname { get; set; }
    public string? MagicDnsName { get; set; }
    public string? TailnetIpv4 { get; set; }
    public DateTime? KeyExpiryUtc { get; set; }
    public bool KeyExpired { get; set; }
    public bool KeyExpiringSoon { get; set; }
    public string? BackendState { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>True when status was inferred from tailscale0 network interface (common in Docker host-network mode).</summary>
    public bool DetectedViaInterface { get; set; }
}
