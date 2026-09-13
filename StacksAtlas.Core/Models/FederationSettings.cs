namespace StacksAtlas.Core.Models;

public class FederationSettings
{
    public ExecutionMode Mode { get; set; } = ExecutionMode.Standalone;
    public string? HubUrl { get; set; }
    public string? FederationToken { get; set; }
    public string NodeId { get; set; } = "node-unnamed";
    public bool AllowUntrustedHubs { get; set; } = true;

    // Hierarchical Location
    public string? Client { get; set; }
    public string? Building { get; set; }
    public string? Room { get; set; }

    // User sync governance
    public bool SyncUsers { get; set; } = false; // Legacy fallback
    public bool SyncUserRegistry { get; set; } = false;
    public bool SyncSsoSettings { get; set; } = false;
    public bool SyncAlertSettings { get; set; } = false;
    public bool SyncSiemSettings { get; set; } = false;

    // Local Overrides
    public bool OverrideAlertSettings { get; set; } = false;
    public bool OverrideSiemSettings { get; set; } = false;
    public bool DelegateAlertDispatch { get; set; } = false;

    /// <summary>Operator-friendly site label (mirrored from Hub on sync).</summary>
    public string? NodeDisplayName { get; set; }

    /// <summary>When true, Node resolves Hub endpoints via tailnet identity instead of LAN/public HubUrl.</summary>
    public bool UseTailscaleForHubConnection { get; set; }

    /// <summary>Hub MagicDNS hostname on the tailnet (e.g. hub.example.ts.net).</summary>
    public string? HubTailscaleMagicDns { get; set; }

    /// <summary>Hub stable Tailscale 100.x.x.x address.</summary>
    public string? HubTailscaleIpv4 { get; set; }
}

