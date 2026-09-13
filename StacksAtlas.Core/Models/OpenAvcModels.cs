namespace StacksAtlas.Core.Models;

public class OpenAvcDeviceInfo
{
    public string Id { get; set; } = "";
    public string? Name { get; set; }
    public string? Ip { get; set; }
    public string? DriverId { get; set; }
    public string? DriverName { get; set; }
    public bool? Connected { get; set; }
}

public class OpenAvcCommandInfo
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
}

public class OpenAvcMacroInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}

public class OpenAvcLinkSuggestion
{
    public string OpenAvcDeviceId { get; set; } = "";
    public string? Name { get; set; }
    public string? Ip { get; set; }
    public string? DriverId { get; set; }
    public string? DriverName { get; set; }
    public string MatchReason { get; set; } = "ip";
}

public class OpenAvcDrawerContext
{
    public bool Configured { get; set; }
    public bool IntegrationEnabled { get; set; }
    public bool Linked { get; set; }
    public bool HubRelay { get; set; }
    public string? OpenAvcDeviceId { get; set; }
    public string? DriverId { get; set; }
    public string? DriverName { get; set; }
    public string? DeviceName { get; set; }
    public OpenAvcLinkSuggestion? Suggestion { get; set; }
    public List<OpenAvcCommandInfo> Commands { get; set; } = [];
    public List<OpenAvcMacroInfo> Macros { get; set; } = [];
    /// <summary>Full project macro catalog (for admin pin UI).</summary>
    public List<OpenAvcMacroInfo> AllMacros { get; set; } = [];
    public List<string> PinnedMacroIds { get; set; } = [];
    public bool HasMacroPins { get; set; }
    public Dictionary<string, string> State { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> DisplayState { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? LastMessage { get; set; }
    public string? LastError { get; set; }
    public string? LastRawResponse { get; set; }
    /// <summary>Deprecated  -  use Readings. Kept for API compat.</summary>
    public string? ParsedSerial { get; set; }
    public List<OpenAvcReading> Readings { get; set; } = [];
    public List<OpenAvcDeviceInfo> AvailableDevices { get; set; } = [];
    /// <summary>online | offline | auth_failed | disabled | hub_relay</summary>
    public string IntegrationStatus { get; set; } = "disabled";
    public bool OpenAvcReachable { get; set; }
    public bool OpenAvcAuthValid { get; set; }
}

public class OpenAvcReading
{
    public string Label { get; set; } = "";
    public string Value { get; set; } = "";
}

public class OpenAvcLinkSummary
{
    public Guid StacksAtlasDeviceId { get; set; }
    public string OpenAvcDeviceId { get; set; } = "";
    public string? DriverName { get; set; }
    public string? DriverId { get; set; }
}

public class OpenAvcNetworkCandidate
{
    public Guid DeviceId { get; set; }
    public string IpAddress { get; set; } = "";
    public string? Hostname { get; set; }
    public string BaseUrl { get; set; } = "";
    public string? Version { get; set; }
    /// <summary>probe | inventory</summary>
    public string Source { get; set; } = "inventory";
}
