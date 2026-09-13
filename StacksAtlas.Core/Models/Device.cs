using System.ComponentModel.DataAnnotations.Schema;
using LiteDB;

namespace StacksAtlas.Core.Models;

public class Device
{
    [BsonId]
    public Guid Id { get; set; } = Guid.NewGuid();
    public string NodeId { get; set; } = string.Empty; // Site ID (Hub-mode primary key component)
    public bool IsDeleted { get; set; } = false;

    /// <summary>
    /// When the device was soft-archived (IsDeleted). Used for permanent purge retention;
    /// do not use LastSeen for purge (a freshly archived offline host would otherwise be deleted immediately).
    /// </summary>
    public DateTime? ArchivedUtc { get; set; }

    // §7.9 Phase B  -  permanent removal tombstone (retained row, hidden from inventory)
    public bool IsPermanentlyRemoved { get; set; } = false;
    public DateTime? RemovedUtc { get; set; }
    public string? RemovedBy { get; set; }
    public string? RemovedReason { get; set; }
    public DateTime? LastRediscoveryAttemptUtc { get; set; }
    public string? LastRediscoveryIp { get; set; }
    public int RediscoveryHitCount { get; set; }

    // Core Networking
    public string IpAddress { get; set; } = string.Empty;
    public string? MacAddress { get; set; }
    public string? Hostname { get; set; }

    // User-Defined / Metadata
    public string Name { get; set; } = string.Empty;
    public string? Location { get; set; }
    public string? Client { get; set; }
    public string? Building { get; set; }
    public string? Room { get; set; }
    public Guid? ManagedByUserId { get; set; }
    public string? ManagedByUsername { get; set; }
    public bool AlertsEnabled { get; set; } = true; // Per-device alert toggle

    /// <summary>Network mode: Unknown, Ethernet, WiFi, Fiber.</summary>
    public string AttachmentKind { get; set; } = "Unknown";
    /// <summary>Port label (e.g. 1/0/12, Gi1/0/12) or Wi-Fi SSID / band.</summary>
    public string? AttachmentPort { get; set; }
    /// <summary>Uplink parent switch/AP/router in inventory (for topology).</summary>
    public Guid? AttachmentParentDeviceId { get; set; }
    /// <summary>Parent MAC for Hub/Node id remapping across federation (preferred).</summary>
    public string? AttachmentParentMac { get; set; }
    /// <summary>Parent IP fallback when MAC is missing (common on some macOS ARP paths).</summary>
    public string? AttachmentParentIp { get; set; }
    public bool IsAttachmentManuallySet { get; set; } = false;

    /// <summary>Resolved uplink display name for API responses (not persisted).</summary>
    [BsonIgnore]
    [NotMapped]
    public string? AttachmentParentName { get; set; }

    // Discovery Data (Mapped from Classifier)
    public string? Vendor { get; set; }
    public string? Type { get; set; }
    public string? Model { get; set; }
    public int ConfidenceScore { get; set; }
    public IdentitySource IdentitySource { get; set; } = IdentitySource.Unknown;
    public string? OperatingSystem { get; set; } // Legacy Lifeboat Support

    // Discovery Interface Provenance (stamped on first discovery, immutable unless manually corrected)
    public string? DiscoveryScopeId { get; set; }
    public string? DiscoveryInterfaceId { get; set; }
    public string? DiscoveryInterfaceName { get; set; }
    public string? DiscoveryVlanTag { get; set; }
    public bool IsDiscoveryProvenanceManuallySet { get; set; } = false;

    // Manual Override Flags (Prevents scanner from overwriting user edits)
    public bool IsModelManuallySet { get; set; } = false;
    public bool IsVendorManuallySet { get; set; } = false;
    public bool IsTypeManuallySet { get; set; } = false;
    public bool IsHostnameManuallySet { get; set; } = false;

    // Asset metadata (serial, tag, firmware, warranty)
    public string? SerialNumber { get; set; }
    public string? AssetTag { get; set; }
    public string? FirmwareVersion { get; set; }
    public DateTime? WarrantyExpiresUtc { get; set; }
    public bool IsSerialNumberManuallySet { get; set; } = false;
    public bool IsFirmwareVersionManuallySet { get; set; } = false;
    public bool IsAssetTagManuallySet { get; set; } = false;
    public bool IsWarrantyExpiresManuallySet { get; set; } = false;
    public AssetMetadataSource AssetMetadataSource { get; set; } = AssetMetadataSource.Unknown;

    // Services
    public List<int> OpenPorts { get; set; } = []; // C# 13 Collection expression

    // Vital Signs
    public string Status { get; set; } = "offline";
    public DateTime FirstSeen { get; set; }
    public DateTime? FirstDiscoveredUtc { get; set; }
    public DateTime LastSeen { get; set; }
    public DateTime LastDbWriteUtc { get; set; } = DateTime.MinValue;
    public DateTime? LastStateChangeUtc { get; set; }
    public DateTime LastModifiedUtc { get; set; } = DateTime.UtcNow;

    // Performance Metrics
    public long? LastLatencyMs { get; set; }
    public double? AverageLatencyMs { get; set; }
    public int StabilityScore { get; set; }
    public List<long> LatencyHistory { get; set; } = [];

    public int TotalSweepsSeen { get; set; }
    public int ScanCount { get; set; } // Explicit count for UI display
    public int TotalSweepsOnline { get; set; }
    public double UptimePercent { get; set; }
    public int FlapCount { get; set; }
    public int CurrentStrikes { get; set; } = 0; // Hysteresis: consecutive offline pings
    public string? LastSweepId { get; set; }

    // Security Assessment
    public SecurityGrade SecurityGrade { get; set; } = SecurityGrade.Green;
    public int SecurityScore { get; set; } = 100;
    public List<string> SecurityIssues { get; set; } = [];

    // Intelligence Findings (Persisted across regular sweeps)
    public List<string> DeepScanIssues { get; set; } = [];

    // SNMP & Recog Discovery Data
    public string? SnmpSysName { get; set; }
    public string? SnmpSysDescr { get; set; }
    public string? HttpTitle { get; set; }
    public DateTime? SnmpLastCheck { get; set; }

    // User Overrides
    public List<string> IgnoredSecurityIssues { get; set; } = [];

    /// <summary>
    /// Transient flag: Node-originated archive / remove / restore governance push (not persisted).
    /// </summary>
    [BsonIgnore]
    [NotMapped]
    public bool IsLifecycleGovernancePush { get; set; }
}
