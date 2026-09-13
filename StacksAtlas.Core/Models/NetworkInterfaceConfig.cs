namespace StacksAtlas.Core.Models;

/// <summary>
/// Operator policy for a physical network adapter: outbound traffic roles and
/// default fleet VLAN label for devices discovered via this interface.
/// </summary>
public class NetworkInterfaceConfig
{
    public string InterfaceId { get; set; } = string.Empty;
    public List<NetworkRole> Roles { get; set; } = [];
    /// <summary>
    /// Default DiscoveryVlanTag for devices found through scopes bound to this adapter.
    /// Scope-level VlanTag overrides when set.
    /// </summary>
    public string? DefaultVlanTag { get; set; }
}
