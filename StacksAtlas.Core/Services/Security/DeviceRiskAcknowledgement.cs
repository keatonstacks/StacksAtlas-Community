using StacksAtlas.Core.Data;
using StacksAtlas.Core.Models;

namespace StacksAtlas.Core.Services.Security;

/// <summary>
/// Shared logic for acknowledging a security finding on a device inventory record.
/// </summary>
public static class DeviceRiskAcknowledgement
{
    public static bool TryAcknowledge(IDeviceRepository repo, Guid deviceId, string issue)
    {
        if (!repo.AcknowledgeSecurityIssue(deviceId, issue))
            return false;

        var device = repo.GetById(deviceId);
        if (device == null || !device.SecurityIssues.Contains(issue))
            return true;

        device.SecurityIssues.Remove(issue);
        device.SecurityGrade = RegradeAfterRemoval(device.SecurityIssues);
        repo.UpsertDevice(device, isUserAction: true);
        return true;
    }

    private static SecurityGrade RegradeAfterRemoval(IReadOnlyList<string> remainingIssues)
    {
        var hasCritical = remainingIssues.Any(i =>
            i.Contains("::Critical::", StringComparison.OrdinalIgnoreCase) ||
            i.Contains("Critical", StringComparison.OrdinalIgnoreCase) ||
            i.Contains("Severe", StringComparison.OrdinalIgnoreCase) ||
            i.Contains("Telnet", StringComparison.OrdinalIgnoreCase) ||
            i.Contains("FTP", StringComparison.OrdinalIgnoreCase));

        var hasWarning = remainingIssues.Any(i =>
            i.Contains("Warning", StringComparison.OrdinalIgnoreCase) ||
            i.Contains("HTTP", StringComparison.OrdinalIgnoreCase));

        if (hasCritical) return SecurityGrade.Red;
        if (hasWarning) return SecurityGrade.Yellow;
        return SecurityGrade.Green;
    }
}
