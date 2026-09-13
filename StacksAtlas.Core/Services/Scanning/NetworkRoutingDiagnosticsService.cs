using Microsoft.Extensions.Logging;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Settings;

namespace StacksAtlas.Core.Services.Scanning;

public interface INetworkRoutingDiagnosticsService
{
    NetworkRoutingDiagnostic Analyze();
}

public interface INetworkRoutingSettingsProvider
{
    NetworkSettings GetSettings();
}

public sealed class NetworkRoutingSettingsProvider(NetworkSettingsStore store) : INetworkRoutingSettingsProvider
{
    public NetworkSettings GetSettings() => store.Load();
}

public sealed class NetworkRoutingDiagnosticsService(
    INetworkInterfaceService networkInterfaceService,
    INetworkRoutingSettingsProvider networkSettingsProvider,
    ISysctlReader sysctlReader,
    ILogger<NetworkRoutingDiagnosticsService> logger) : INetworkRoutingDiagnosticsService
{
    private readonly INetworkInterfaceService _networkInterfaceService = networkInterfaceService;
    private readonly INetworkRoutingSettingsProvider _networkSettingsProvider = networkSettingsProvider;
    private readonly ISysctlReader _sysctlReader = sysctlReader;
    private readonly ILogger<NetworkRoutingDiagnosticsService> _logger = logger;

    public NetworkRoutingDiagnostic Analyze()
    {
        var interfaces = _networkInterfaceService.GetAllInterfaces();
        var settings = _networkSettingsProvider.GetSettings();
        var discoveryInterfaceIds = ResolveDiscoveryInterfaceIds(settings);
        var isMultiHomed = interfaces.Count >= 2 || discoveryInterfaceIds.Count >= 2;

        if (!OperatingSystem.IsLinux())
        {
            return new NetworkRoutingDiagnostic
            {
                IsLinux = false,
                IsMultiHomedProfile = isMultiHomed,
                PhysicalInterfaceCount = interfaces.Count,
                DistinctDiscoveryInterfaceCount = discoveryInterfaceIds.Count,
                RpFilterCheckApplicable = false,
                Severity = "Info",
                Summary = isMultiHomed
                    ? "Multi-NIC profile detected. Reverse-path filtering checks apply to Linux hosts only."
                    : "Single-interface profile. Reverse-path filtering checks apply to Linux hosts only.",
                Detail = "Linux rp_filter mitigation is not required on Windows or macOS hosts."
            };
        }

        var allValue = _sysctlReader.TryReadRpFilter("all");
        var defaultValue = _sysctlReader.TryReadRpFilter("default");
        var perInterface = BuildPerInterfaceStatuses(interfaces, discoveryInterfaceIds);
        var effectiveMode = ResolveEffectiveMode(allValue, defaultValue, perInterface.Where(i => i.UsedForDiscovery));
        var requiresRemediation = isMultiHomed && effectiveMode == "Strict";

        var diagnostic = new NetworkRoutingDiagnostic
        {
            IsLinux = true,
            IsMultiHomedProfile = isMultiHomed,
            PhysicalInterfaceCount = interfaces.Count,
            DistinctDiscoveryInterfaceCount = discoveryInterfaceIds.Count,
            RpFilterCheckApplicable = true,
            AllRpFilterValue = allValue,
            DefaultRpFilterValue = defaultValue,
            RpFilterMode = effectiveMode,
            RequiresRemediation = requiresRemediation,
            Severity = requiresRemediation ? "Warning" : "Info",
            PerInterface = perInterface,
            SysctlCommands =
            [
                "sudo sysctl -w net.ipv4.conf.all.rp_filter=2",
                "sudo sysctl -w net.ipv4.conf.default.rp_filter=2"
            ],
            RemediationSteps =
            [
                "Apply loose reverse-path filtering (rp_filter=2) on the Linux host.",
                "Persist the setting in /etc/sysctl.d/99-stacksatlas-multinic.conf so it survives reboots.",
                "Restart networking or reboot the host if discovery results do not improve immediately.",
                "For Docker deployments, run the container with host networking or set STACKSATLAS_APPLY_RP_FILTER=1 on a privileged host."
            ]
        };

        diagnostic = diagnostic with
        {
            Summary = BuildSummary(diagnostic),
            Detail = BuildDetail(diagnostic)
        };

        if (requiresRemediation)
        {
            _logger.LogWarning(
                "Multi-NIC Linux host is using strict rp_filter (all={AllValue}, default={DefaultValue}). Discovery replies may be dropped.",
                allValue,
                defaultValue);
        }

        return diagnostic;
    }

    private static HashSet<string> ResolveDiscoveryInterfaceIds(NetworkSettings settings)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var scope in settings.Subnets)
        {
            var resolved = scope.ResolveInterfaceId(settings.InterfaceRoleMappings);
            if (!string.IsNullOrWhiteSpace(resolved))
                ids.Add(resolved);
        }

        return ids;
    }

    private List<InterfaceRpFilterStatus> BuildPerInterfaceStatuses(
        List<NetworkInterfaceInfo> interfaces,
        HashSet<string> discoveryInterfaceIds)
    {
        return interfaces
            .Select(iface =>
            {
                var value = _sysctlReader.TryReadRpFilter(iface.Name);
                return new InterfaceRpFilterStatus
                {
                    InterfaceName = iface.Name,
                    InterfaceId = iface.Id,
                    IpAddress = iface.IpAddress,
                    RpFilterValue = value,
                    Mode = DescribeRpFilterValue(value),
                    UsedForDiscovery = discoveryInterfaceIds.Contains(iface.Id)
                };
            })
            .OrderByDescending(i => i.UsedForDiscovery)
            .ThenBy(i => i.InterfaceName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ResolveEffectiveMode(
        int? allValue,
        int? defaultValue,
        IEnumerable<InterfaceRpFilterStatus> discoveryInterfaces)
    {
        if (allValue == 2 || defaultValue == 2)
            return "Loose";

        var discoveryValues = discoveryInterfaces
            .Select(i => i.RpFilterValue)
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .ToList();

        if (discoveryValues.Any(v => v == 1) || allValue == 1 || defaultValue == 1)
            return "Strict";

        if (discoveryValues.Any(v => v == 2) || allValue == 0 || defaultValue == 0)
            return discoveryValues.Any(v => v == 2) ? "Loose" : "Disabled";

        return "Unknown";
    }

    private static string DescribeRpFilterValue(int? value) => value switch
    {
        0 => "Disabled",
        1 => "Strict",
        2 => "Loose",
        _ => "Unknown"
    };

    private static string BuildSummary(NetworkRoutingDiagnostic diagnostic)
    {
        if (!diagnostic.IsMultiHomedProfile)
            return "Single-interface profile detected. No Linux rp_filter remediation required.";

        if (!diagnostic.RequiresRemediation)
            return $"Multi-NIC profile detected. Linux rp_filter is already permissive ({diagnostic.RpFilterMode} mode).";

        return "Multi-NIC profile detected on Linux with strict rp_filter enabled. Discovery replies may be silently dropped.";
    }

    private static string BuildDetail(NetworkRoutingDiagnostic diagnostic)
    {
        if (!diagnostic.IsMultiHomedProfile)
        {
            return "Reverse-path filtering only affects hosts with multiple active adapters or multiple discovery-bound interfaces.";
        }

        if (!diagnostic.RequiresRemediation)
        {
            return "Loose or disabled reverse-path filtering allows scan replies to return on a different adapter than the outbound probe.";
        }

        return "Strict reverse-path filtering drops inbound discovery replies when Linux receives them on a different interface than the outbound sweep. Set net.ipv4.conf.all.rp_filter=2 (Loose Mode) on the host.";
    }
}
