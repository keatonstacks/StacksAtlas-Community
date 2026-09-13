using System.Text.Json.Serialization;

namespace StacksAtlas.Core.Models;

[JsonConverter(typeof(NetworkScopeConverter))]
public class NetworkScope
{
    /// <summary>
    /// Stable identifier for this scan scope. Auto-assigned when missing during settings validation.
    /// </summary>
    public string Id { get; set; } = string.Empty;
    public string Cidr { get; set; } = string.Empty;
    public string? InterfaceId { get; set; } // Null/empty means "Auto-Detect"
    public bool EnableArp { get; set; } = true;
    public bool EnablePing { get; set; } = true;
    public bool EnableMdns { get; set; } = true;
    public bool EnableUpnp { get; set; } = true;

    /// <summary>
    /// Optional operator-assigned VLAN identifier (numeric ID or friendly label) for fleet reporting.
    /// </summary>
    public string? VlanTag { get; set; }

    /// <summary>
    /// Resolves the actual InterfaceId to use based on settings mappings.
    /// If InterfaceId is null/empty, falls back to the interface mapped to NetworkRole.DiscoveryFallback.
    /// </summary>
    public string? ResolveInterfaceId(List<InterfaceRoleMapping> mappings)
    {
        if (!string.IsNullOrWhiteSpace(InterfaceId))
        {
            return InterfaceId;
        }

        var fallbackMapping = mappings?.FirstOrDefault(m => m.Role == NetworkRole.DiscoveryFallback);
        return fallbackMapping?.InterfaceId;
    }
}
