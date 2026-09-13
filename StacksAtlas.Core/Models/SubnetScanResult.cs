namespace StacksAtlas.Core.Models
{
    public record SubnetScanResult(
    string Cidr,
    int TotalHosts,
    int OnlineHosts,
    long DurationMs,
    List<HostScanResult> Hosts,
    string? Error,
    string? DiscoveryScopeId = null,
    string? DiscoveryInterfaceId = null,
    string? DiscoveryInterfaceName = null,
    string? DiscoveryVlanTag = null
    );
}
