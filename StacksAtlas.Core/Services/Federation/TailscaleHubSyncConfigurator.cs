using Microsoft.Extensions.Logging;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Services.Scanning;
using StacksAtlas.Core.Settings;

namespace StacksAtlas.Core.Services.Federation;

/// <summary>
/// Ensures Hub Sync outbound traffic uses the Tailscale adapter when tailnet transport is enabled.
/// </summary>
public static class TailscaleHubSyncConfigurator
{
    public static bool ApplyToNetworkSettings(
        NetworkSettings settings,
        INetworkInterfaceService interfaceService,
        ILogger logger)
    {
        var tailscale = interfaceService.GetTailscaleInterface();
        if (tailscale == null)
        {
            logger.LogWarning(
                "Tailscale transport enabled but no Tailscale adapter detected; Hub Sync role was not auto-assigned.");
            return false;
        }

        settings.InterfaceConfigs ??= [];

        foreach (var config in settings.InterfaceConfigs)
        {
            config.Roles ??= [];
            config.Roles.RemoveAll(r => r == NetworkRole.HubCommunication);
        }

        var tailscaleConfig = settings.InterfaceConfigs
            .FirstOrDefault(c => c.InterfaceId == tailscale.Id);

        if (tailscaleConfig == null)
        {
            tailscaleConfig = new NetworkInterfaceConfig { InterfaceId = tailscale.Id, Roles = [] };
            settings.InterfaceConfigs.Add(tailscaleConfig);
        }

        if (!tailscaleConfig.Roles.Contains(NetworkRole.HubCommunication))
            tailscaleConfig.Roles.Add(NetworkRole.HubCommunication);

        logger.LogInformation(
            "Auto-assigned Hub Sync role to Tailscale adapter {InterfaceName} ({Ip}) for tailnet federation transport.",
            tailscale.Name,
            tailscale.IpAddress);

        return true;
    }
}
