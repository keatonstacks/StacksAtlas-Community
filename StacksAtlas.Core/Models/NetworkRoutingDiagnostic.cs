namespace StacksAtlas.Core.Models;

public record InterfaceRpFilterStatus
{
    public string InterfaceName { get; init; } = string.Empty;
    public string? InterfaceId { get; init; }
    public string? IpAddress { get; init; }
    public int? RpFilterValue { get; init; }
    public string Mode { get; init; } = "Unknown";
    public bool UsedForDiscovery { get; init; }
}

public record NetworkRoutingDiagnostic
{
    public bool IsLinux { get; init; }
    public bool IsMultiHomedProfile { get; init; }
    public int PhysicalInterfaceCount { get; init; }
    public int DistinctDiscoveryInterfaceCount { get; init; }
    public bool RpFilterCheckApplicable { get; init; }
    public int? AllRpFilterValue { get; init; }
    public int? DefaultRpFilterValue { get; init; }
    public string RpFilterMode { get; init; } = "Unknown";
    public bool RequiresRemediation { get; init; }
    public string Severity { get; init; } = "Info";
    public string Summary { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public List<string> RemediationSteps { get; init; } = [];
    public List<string> SysctlCommands { get; init; } = [];
    public List<InterfaceRpFilterStatus> PerInterface { get; init; } = [];
}
