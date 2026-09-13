using StacksAtlas.Core.Models;
using StacksAtlas.Core.Services.Devices;

namespace StacksAtlas.Core.Services.Network;

/// <summary>
/// Resolves the gateway/root device for a scoped topology view (Hub fleet site or local inventory).
/// </summary>
public static class TopologyGatewayResolver
{
    public static Device? ResolveSiteGateway(IReadOnlyList<Device> devices)
    {
        if (devices.Count == 0)
            return null;

        var active = devices.Where(d => !d.IsDeleted && !d.IsPermanentlyRemoved).ToList();

        var gatewayLike = active.FirstOrDefault(d => IsGatewayLikeType(d.Type));
        if (gatewayLike != null)
            return gatewayLike;

        return active
            .Where(d => IsInfrastructureType(d.Type))
            .OrderByDescending(d => CountAttachmentChildren(d, active))
            .FirstOrDefault();
    }

    public static Device? ResolveByGatewayIp(IReadOnlyList<Device> devices, string? gatewayIp)
    {
        if (string.IsNullOrWhiteSpace(gatewayIp))
            return null;
        return devices.FirstOrDefault(d =>
            string.Equals(d.IpAddress, gatewayIp, StringComparison.OrdinalIgnoreCase));
    }

    private static int CountAttachmentChildren(Device parent, IReadOnlyList<Device> devices)
    {
        var parentMac = DeviceMacNormalizer.Normalize(parent.MacAddress);
        return devices.Count(child =>
            child.AttachmentParentDeviceId == parent.Id
            || (!string.IsNullOrWhiteSpace(child.AttachmentParentMac)
                && DeviceMacNormalizer.Normalize(child.AttachmentParentMac) == parentMac));
    }

    private static bool IsGatewayLikeType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
            return false;
        var t = type.Trim();
        return t.Contains("Router", StringComparison.OrdinalIgnoreCase)
            || t.Contains("Gateway", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInfrastructureType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
            return false;
        var t = type.Trim();
        if (t.StartsWith("Network", StringComparison.OrdinalIgnoreCase))
            return true;
        return t.Equals("Switch", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Router", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Gateway", StringComparison.OrdinalIgnoreCase)
            || t.Equals("AP", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Firewall", StringComparison.OrdinalIgnoreCase);
    }
}
