using Microsoft.Extensions.Logging;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Services.Scanning;

namespace StacksAtlas.Core.Settings;

/// <summary>
/// Ensures at least one scan scope exists before discovery workers start.
/// </summary>
public static class NetworkDiscoveryScopeBootstrap
{
    public static bool EnsureAtLeastOneSubnet(
        NetworkSettingsStore store,
        INetworkService networkService,
        ILogger? logger = null)
    {
        var settings = store.Load();
        if (settings.Subnets.Count > 0)
            return false;

        var cidr = networkService.GetScannerCidr();
        if (!IsUsableCidr(cidr))
            return false;

        settings.Subnets.Add(new NetworkScope { Cidr = cidr });
        store.Save(settings);
        logger?.LogInformation("Seeded discovery scope {Cidr} for onboarding.", cidr);
        return true;
    }

    internal static bool IsUsableCidr(string? cidr) =>
        !string.IsNullOrWhiteSpace(cidr)
        && !string.Equals(cidr, "N/A", StringComparison.OrdinalIgnoreCase);
}
