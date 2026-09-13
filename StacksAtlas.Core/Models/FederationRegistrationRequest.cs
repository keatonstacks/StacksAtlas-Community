namespace StacksAtlas.Core.Models;

public class FederationRegistrationRequest
{
    public string NodeId { get; set; } = string.Empty;
    public string FederationToken { get; set; } = string.Empty;
    public string? Version { get; set; }
    public string? OS { get; set; }
    public string? LicenseTier { get; set; }

    /// <summary>
    /// True when the Node is running portable/session mode (always Free/Home entitlements locally).
    /// </summary>
    public bool IsPortable { get; set; }

    public int HttpPort { get; set; } = 5000;
    public int HttpsPort { get; set; } = 5001;

    // Hierarchy Metadata
    public string? Client { get; set; }
    public string? Building { get; set; }
    public string? Room { get; set; }

    // Overrides
    public bool OverrideAlertSettings { get; set; }
    public bool OverrideSiemSettings { get; set; }

    // Local Scanning Config
    public string? ScanSettingsJson { get; set; }

    // Edge Users Migration
    public List<User>? LocalUsersToImport { get; set; }

    // Telemetry metrics
    public long DatabaseSize { get; set; }

    // Hardware Signature
    public string? HardwareId { get; set; }

    /// <summary>
    /// True when the node is enrolled but local onboarding/foundation is incomplete
    /// (e.g. after Hub-initiated site reset recovery).
    /// </summary>
    public bool RequiresOperatorSetup { get; set; }
}
