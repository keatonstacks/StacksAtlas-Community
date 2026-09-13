using System;

namespace StacksAtlas.Core.Models;

public class FederatedNode
{
    public string Id { get; set; } = string.Empty; // User-defined ID (e.g. "Site-A")
    public string Name { get; set; } = string.Empty;
    public string? IPAddress { get; set; }
    public string? Status { get; set; } = "Offline";
    public DateTime LastSeenUtc { get; set; }
    public DateTime LastSyncUtc { get; set; }
    public string? Version { get; set; }
    public string? OS { get; set; }
    
    // Hierarchical Location
    public string? Client { get; set; }
    public string? Building { get; set; }
    public string? Room { get; set; }
    
    // License Info (Mirror from Node)
    public string? LicenseTier { get; set; }

    /// <summary>True when the remote site reported portable/session install mode.</summary>
    public bool IsPortable { get; set; }
    
    // Connection Info
    public string? ConnectionId { get; set; } // SignalR Connection ID
    public int HttpPort { get; set; } = 5000;
    public int HttpsPort { get; set; } = 5001;

    // Federation & Security Governance
    public bool SyncUsers { get; set; } = false; // Legacy fallback
    public bool SyncUserRegistry { get; set; } = false;
    public bool SyncSsoSettings { get; set; } = false;
    public bool SyncAlertSettings { get; set; } = false;
    public bool SyncSiemSettings { get; set; } = false;
    public bool OverrideAlertSettings { get; set; } = false;
    public bool OverrideSiemSettings { get; set; } = false;
    public bool IsIdentityImported { get; set; } = false;
    public bool DelegateAlertDispatch { get; set; } = false;

 
    // Centralized Scanning Control
    public string? ScanSettingsJson { get; set; }

    /// <summary>Last known Node debug-logging state (telemetry from Node, or Hub toggle).</summary>
    public bool IsDebugLoggingEnabled { get; set; }

    // Telemetry Metrics
    public long DatabaseSize { get; set; }

    // mTLS Sovereign Enrollment
    public string? CertificateSerialNumber { get; set; }
    public bool IsRevoked { get; set; } = false;

    // Hardware Signature
    public string? HardwareId { get; set; }
}

