using StacksAtlas.Core.Models;

namespace StacksAtlas.Core.Services.Devices;

/// <summary>
/// Shared rules for devices that may be selected as an attachment uplink parent.
/// </summary>
public static class DeviceAttachmentParentCatalog
{
    private static readonly string[] InfraTypePrefixes =
    [
        "Network Infrastructure",
        "Network Router",
        "Network Switch",
        "Network AP",
        "Network Gateway",
        "Network Controller",
        "Network Firewall",
    ];

    public static bool IsInfrastructureType(string? type)
    {
        var label = FormatDeviceTypeLabel(type);
        return InfraTypePrefixes.Any(
            prefix => label.Equals(prefix, StringComparison.OrdinalIgnoreCase)
                      || label.StartsWith($"{prefix} ", StringComparison.OrdinalIgnoreCase));
    }

    public static string FormatDeviceTypeLabel(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
            return "Unknown";

        var normalized = type.Replace('_', ' ').Trim();
        return System.Text.RegularExpressions.Regex.Replace(
            normalized,
            "(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])",
            " ");
    }

    public static string DisplayLabel(Device device)
    {
        if (!string.IsNullOrWhiteSpace(device.Name))
            return device.Name.Trim();
        if (!string.IsNullOrWhiteSpace(device.Hostname))
            return device.Hostname.Trim();
        return device.IpAddress?.Trim() ?? device.Id.ToString();
    }

    public static IEnumerable<Device> FilterCandidates(
        IEnumerable<Device> devices,
        Guid? excludeDeviceId = null)
    {
        return devices
            .Where(d => excludeDeviceId == null || d.Id != excludeDeviceId.Value)
            .Where(d => !d.IsDeleted && !d.IsPermanentlyRemoved)
            .Where(d => IsInfrastructureType(d.Type))
            .OrderBy(DisplayLabel, StringComparer.OrdinalIgnoreCase);
    }
}
