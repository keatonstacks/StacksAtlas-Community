namespace StacksAtlas.Core.Models;

public enum NetworkInterfaceScope
{
    /// <summary>Physical adapters for discovery scope binding (excludes tunnel/virtual overlays).</summary>
    Discovery,

    /// <summary>All operator-configurable adapters including Tailscale transport (tailscale0).</summary>
    Policy
}
