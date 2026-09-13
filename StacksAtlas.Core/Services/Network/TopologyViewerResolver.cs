using System.Net;
using StacksAtlas.Core.Models;

namespace StacksAtlas.Core.Services.Network;

/// <summary>
/// Maps the browser/API client to a topology node for "you are here" highlighting.
/// </summary>
public static class TopologyViewerResolver
{
    public static string? ResolveViewerNodeId(
        string? clientIp,
        string? activeInterfaceIp,
        IEnumerable<Device> devices,
        bool includeHostMachineNode)
    {
        var normalizedClient = NormalizeIp(clientIp);
        if (string.IsNullOrEmpty(normalizedClient))
            return null;

        var deviceList = devices as IReadOnlyList<Device> ?? devices.ToList();

        if (IsLoopback(normalizedClient))
        {
            var applianceIp = NormalizeIp(activeInterfaceIp);
            if (string.IsNullOrEmpty(applianceIp) || applianceIp == "0.0.0.0")
                return null;

            var applianceDevice = deviceList.FirstOrDefault(d =>
                string.Equals(NormalizeIp(d.IpAddress), applianceIp, StringComparison.OrdinalIgnoreCase));
            if (applianceDevice != null)
                return applianceDevice.Id.ToString();

            return includeHostMachineNode ? "host_machine" : null;
        }

        var viewerDevice = deviceList.FirstOrDefault(d =>
            string.Equals(NormalizeIp(d.IpAddress), normalizedClient, StringComparison.OrdinalIgnoreCase));
        return viewerDevice?.Id.ToString();
    }

    public static string? NormalizeIp(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip))
            return null;

        var trimmed = ip.Trim();
        if (trimmed.StartsWith("::ffff:", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed["::ffff:".Length..];

        return IPAddress.TryParse(trimmed, out var parsed)
            ? parsed.MapToIPv4().ToString()
            : trimmed;
    }

    public static bool IsLoopback(string normalizedIp) =>
        normalizedIp == "127.0.0.1"
        || normalizedIp == "::1"
        || normalizedIp.StartsWith("127.", StringComparison.Ordinal);
}
