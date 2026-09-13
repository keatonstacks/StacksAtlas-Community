using System;
using System.Linq;
using StacksAtlas.Core.Abstractions;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Settings;
using Microsoft.Extensions.Logging;

namespace StacksAtlas.Core.Services.Devices;

public class DeviceReconciliationService
{
    private readonly RecogMatchingService _recogService;
    private readonly IntelligenceEngine _intelligence;
    private readonly ILogger<DeviceReconciliationService> _logger;
    private readonly IClock _clock;
    private readonly FederationSettingsStore _settingsStore;

    public DeviceReconciliationService(
        RecogMatchingService recogService, 
        IntelligenceEngine intelligence,
        ILogger<DeviceReconciliationService> logger,
        IClock clock,
        FederationSettingsStore settingsStore)
    {
        _recogService = recogService;
        _intelligence = intelligence;
        _logger = logger;
        _clock = clock;
        _settingsStore = settingsStore;
    }

    /// <summary>
    /// Reconciles an incoming device update against an existing device record, applying classification
    /// rules, manual override protection, and timestamp updates.
    /// Returns the mutated existing device.
    /// </summary>
    public Device ReconcileExisting(Device existing, Device incoming, bool isUserAction)
    {
        // --- 1. NAME PROTECTION ---
        bool incomingIsGeneric = IsGenericName(incoming.Name, incoming.IpAddress, incoming.Hostname);
        bool existingHasManualName = !IsGenericName(existing.Name, existing.IpAddress, existing.Hostname);

        bool isHub = StacksAtlas.Core.State.ExecutionState.IsHub;

        if (isUserAction && !string.IsNullOrWhiteSpace(incoming.Name))
        {
            existing.Name = incoming.Name;
        }
        else if (isHub)
        {
            // Hub trusts the federated Node's telemetry payload absolutely.
            // The Node has already run its own local human protection shield.
            existing.Name = !string.IsNullOrWhiteSpace(incoming.Name) ? incoming.Name : existing.Name;
        }
        else if (existingHasManualName && incomingIsGeneric)
        {
            // Human Protection Shield (Node/Standalone only): Never overwrite a manual name with a generic one
            incoming.Name = existing.Name;
        }
        else if (!incomingIsGeneric)
        {
            // Incoming has a better name
            existing.Name = incoming.Name;
        }

        // --- 2. INTELLIGENCE GATHERING (WEIGHTED HINTS) ---
        var hints = new List<ClassificationHint>();
        var hintName = !string.IsNullOrWhiteSpace(incoming.Name) ? incoming.Name : existing.Name;
        var hintHostname = !string.IsNullOrWhiteSpace(incoming.Hostname) ? incoming.Hostname : existing.Hostname;

        // Hint Source A: Name & Hostname Patterns
        var nameHints = _intelligence.GetHints(hintName, hintHostname, null, existing.MacAddress, null);
        hints.AddRange(nameHints);

        // Hint Source D: OUI Lookup (before ports so port rules can veto conflicts)
        var (ouiVendor, ouiType) = OuiDatabase.Lookup(existing.MacAddress);
        if (ouiVendor != "Unknown Vendor")
        {
            hints.Add(new ClassificationHint(ouiType, ouiVendor, null, 95, "OUI"));
        }

        // Hint Source B: Port Fingerprints
        var portHints = _intelligence.GetHints(
            null,
            null,
            ouiVendor != "Unknown Vendor" ? ouiVendor : null,
            existing.MacAddress,
            incoming.OpenPorts ?? existing.OpenPorts);
        hints.AddRange(portHints);

        // Hint Source C: Recog (HTTP Title & SNMP)
        var recogHints = _recogService.GetHints(incoming.HttpTitle ?? existing.HttpTitle, incoming.SnmpSysDescr ?? existing.SnmpSysDescr);
        hints.AddRange(recogHints);

        // --- 3. SYNTHESIS ---
        var (refinedType, refinedModel, refinedVendor, confidence, incomingSource) = _intelligence.Synthesize(hints);

        if (isUserAction) incomingSource = IdentitySource.Manual;

        // --- 4. APPLY CLASSIFICATION (With Specificity Locking) ---
        // RULE: We only update the identity if the incoming source is HIGHER or EQUAL rank to existing.
        // This prevents a generic OUI (Tier 1) from overwriting a Recog match (Tier 4).
        
        bool existingIsUnknown = string.IsNullOrWhiteSpace(existing.Type) || 
                               existing.Type.Equals("Unknown", StringComparison.OrdinalIgnoreCase);
        
        bool sourceIsBetterOrEqual = incomingSource >= existing.IdentitySource;
        bool foundBetterType = refinedType != DeviceType.Unknown && confidence >= 30;
        bool authoritativeTypeCorrection = !existing.IsTypeManuallySet
            && IntelligenceEngine.ShouldAllowTypeCorrection(hints, refinedType, existing.Type);

        if (!existing.IsTypeManuallySet && !isUserAction)
        {
            if ((foundBetterType || existingIsUnknown) && (sourceIsBetterOrEqual || authoritativeTypeCorrection))
            {
                if (existing.IdentitySource < incomingSource && incomingSource > IdentitySource.Discovery)
                {
                    _logger.LogDebug("Reconciliation [{Ip}]: Identity Upgraded from {OldSource} to {NewSource} ({Vendor} {Model})", 
                        existing.IpAddress, existing.IdentitySource, incomingSource, refinedVendor, refinedModel);
                }

                if (authoritativeTypeCorrection)
                {
                    _logger.LogDebug(
                        "Reconciliation [{Ip}]: Corrected type {OldType} → {NewType} via authoritative hints",
                        existing.IpAddress,
                        existing.Type,
                        IntelligenceEngine.FormatType(refinedType.ToString()));
                }

                existing.Type = IntelligenceEngine.FormatType(refinedType.ToString());
                if (!existing.IsModelManuallySet)
                {
                    if (!string.IsNullOrWhiteSpace(refinedModel))
                        existing.Model = refinedModel;
                    else if (IntelligenceEngine.ShouldClearStaleProAvModel(existing.Model, refinedType))
                        existing.Model = null;
                }
                existing.IdentitySource = incomingSource;
                existing.ConfidenceScore = confidence;
            }
            else if (!sourceIsBetterOrEqual && foundBetterType && !authoritativeTypeCorrection)
            {
                _logger.LogTrace("Reconciliation [{Ip}]: Blocked Specificity Downgrade. Kept {OldSource} identity over {NewSource}.", 
                    existing.IpAddress, existing.IdentitySource, incomingSource);
            }
        }
        
        bool existingVendorIsUnknown = string.IsNullOrWhiteSpace(existing.Vendor) ||
                                     existing.Vendor.Equals("Unknown Vendor", StringComparison.OrdinalIgnoreCase);

        if (!existing.IsVendorManuallySet && !isUserAction)
        {
            if (!string.IsNullOrWhiteSpace(refinedVendor) && 
                (confidence >= 30 || existingVendorIsUnknown) && sourceIsBetterOrEqual)
            {
                existing.Vendor = refinedVendor;
            }
        }

        // Handle User Overrides
        if (isUserAction)
        {
            if (!string.IsNullOrWhiteSpace(incoming.Model))
            {
                existing.Model = incoming.Model;
                existing.IsModelManuallySet = true;
                existing.ConfidenceScore = 100; // Manual is always 100
            }
            if (!string.IsNullOrWhiteSpace(incoming.Vendor))
            {
                existing.Vendor = incoming.Vendor;
                existing.IsVendorManuallySet = true;
            }
            if (!string.IsNullOrWhiteSpace(incoming.Type))
            {
                existing.Type = incoming.Type;
                existing.IsTypeManuallySet = true;
                existing.IdentitySource = IdentitySource.Manual;
                existing.ConfidenceScore = 100;
            }
        }
        else if (isHub)
        {
            // Hub trusts the federated Node absolutely, bypassing local protection.
            existing.Model = incoming.Model ?? existing.Model;
            existing.IsModelManuallySet = incoming.IsModelManuallySet;
            
            existing.Vendor = incoming.Vendor ?? existing.Vendor;
            existing.IsVendorManuallySet = incoming.IsVendorManuallySet;

            existing.IsTypeManuallySet = incoming.IsTypeManuallySet;

            existing.Hostname = incoming.Hostname ?? existing.Hostname;
            existing.IsHostnameManuallySet = incoming.IsHostnameManuallySet;
            
            existing.Type = incoming.Type ?? existing.Type;

            existing.SerialNumber = incoming.SerialNumber ?? existing.SerialNumber;
            existing.AssetTag = incoming.AssetTag ?? existing.AssetTag;
            existing.FirmwareVersion = incoming.FirmwareVersion ?? existing.FirmwareVersion;
            existing.WarrantyExpiresUtc = incoming.WarrantyExpiresUtc ?? existing.WarrantyExpiresUtc;
            existing.IsSerialNumberManuallySet = incoming.IsSerialNumberManuallySet;
            existing.IsFirmwareVersionManuallySet = incoming.IsFirmwareVersionManuallySet;
            existing.IsAssetTagManuallySet = incoming.IsAssetTagManuallySet;
            existing.IsWarrantyExpiresManuallySet = incoming.IsWarrantyExpiresManuallySet;
            existing.AssetMetadataSource = incoming.AssetMetadataSource;

            // Manual attachment from Node. Kind/port always apply.
            // Parent is MAC-first; never let an incomplete Node snapshot wipe a Hub-set uplink
            // (Hub/Node GUIDs diverge; failed parent resolve used to null out Hub parents).
            if (incoming.IsAttachmentManuallySet)
            {
                existing.AttachmentKind = string.IsNullOrWhiteSpace(incoming.AttachmentKind)
                    ? "Unknown"
                    : incoming.AttachmentKind;
                existing.AttachmentPort = incoming.AttachmentPort;
                existing.IsAttachmentManuallySet = true;

                // Cross-node parent identity: MAC preferred, IP fallback. Node GUIDs alone
                // must never overwrite Hub parent ids (shows as truncated ids like "80c8e656").
                var incomingParentMac = DeviceMacNormalizer.Normalize(incoming.AttachmentParentMac);
                var incomingParentIp = string.IsNullOrWhiteSpace(incoming.AttachmentParentIp)
                    ? null
                    : incoming.AttachmentParentIp.Trim();
                if (!string.IsNullOrEmpty(incomingParentMac) || !string.IsNullOrEmpty(incomingParentIp))
                {
                    existing.AttachmentParentDeviceId = incoming.AttachmentParentDeviceId;
                    existing.AttachmentParentMac = string.IsNullOrEmpty(incomingParentMac) ? null : incomingParentMac;
                    existing.AttachmentParentIp = incomingParentIp;
                }
                // else: keep existing Hub uplink (GUID-only Node snapshots are incomplete).
            }
            
            existing.ConfidenceScore = incoming.ConfidenceScore;
            existing.IdentitySource = incoming.IdentitySource;
        }

        // --- 5. UPDATE VOLATILE FIELDS ---
        // Guard: Only overwrite volatile scan data when values are present.
        // This prevents user-action partial updates from silently wiping scan state.
        existing.Status = incoming.Status ?? existing.Status;
        existing.IpAddress = incoming.IpAddress ?? existing.IpAddress;
        if (isUserAction)
        {
            existing.Hostname = incoming.Hostname;
            existing.IsHostnameManuallySet = incoming.IsHostnameManuallySet;
        }
        else if (!existing.IsHostnameManuallySet && !string.IsNullOrWhiteSpace(incoming.Hostname))
        {
            existing.Hostname = incoming.Hostname;
        }

        if (!string.IsNullOrWhiteSpace(incoming.Status) && 
            incoming.Status.Equals("online", StringComparison.OrdinalIgnoreCase))
        {
            existing.LastSeen = _clock.UtcNow;
        }

        existing.LastLatencyMs = incoming.LastLatencyMs ?? existing.LastLatencyMs;
        existing.AverageLatencyMs = incoming.AverageLatencyMs ?? existing.AverageLatencyMs;
        existing.OpenPorts = incoming.OpenPorts ?? existing.OpenPorts;
        existing.StabilityScore = incoming.StabilityScore > 0 ? incoming.StabilityScore : existing.StabilityScore;
        existing.UptimePercent = incoming.UptimePercent > 0 ? incoming.UptimePercent : existing.UptimePercent;
        existing.TotalSweepsSeen = Math.Max(existing.TotalSweepsSeen, incoming.TotalSweepsSeen);
        existing.ScanCount = Math.Max(existing.ScanCount, incoming.ScanCount);
        existing.TotalSweepsOnline = Math.Max(existing.TotalSweepsOnline, incoming.TotalSweepsOnline);
        existing.FlapCount = incoming.FlapCount > 0 ? incoming.FlapCount : existing.FlapCount;
        existing.LastStateChangeUtc = incoming.LastStateChangeUtc ?? existing.LastStateChangeUtc;
        existing.LastSweepId = incoming.LastSweepId ?? existing.LastSweepId;
        existing.SecurityGrade = incoming.SecurityGrade;
        if (incoming.SecurityIssues is { Count: > 0 })
        {
            existing.SecurityIssues = incoming.SecurityIssues;
        }
        else if (incoming.SecurityGrade == SecurityGrade.Green)
        {
            existing.SecurityIssues = incoming.SecurityIssues ?? [];
        }

        existing.OperatingSystem = incoming.OperatingSystem ?? existing.OperatingSystem;

        if (incoming.DeepScanIssues is { Count: > 0 })
        {
            existing.DeepScanIssues = incoming.DeepScanIssues;
        }
        else if (incoming.SecurityGrade == SecurityGrade.Green && incoming.DeepScanIssues != null)
        {
            existing.DeepScanIssues = incoming.DeepScanIssues;
        }

        if (isHub)
        {
            existing.IgnoredSecurityIssues ??= [];
            if (incoming.IgnoredSecurityIssues is { Count: > 0 })
            {
                foreach (var ignored in incoming.IgnoredSecurityIssues)
                {
                    if (!existing.IgnoredSecurityIssues.Contains(ignored))
                        existing.IgnoredSecurityIssues.Add(ignored);
                }
            }

            if (existing.IgnoredSecurityIssues.Count > 0 && existing.SecurityIssues is { Count: > 0 })
            {
                existing.SecurityIssues = existing.SecurityIssues
                    .Where(i => !existing.IgnoredSecurityIssues.Contains(i))
                    .ToList();
            }
        }

        // Reconcile FirstDiscoveredUtc (earliest wins, falling back to FirstSeen)
        var existingEffectiveDiscovered = (existing.FirstDiscoveredUtc == null || existing.FirstDiscoveredUtc == DateTime.MinValue)
            ? (existing.FirstSeen == DateTime.MinValue ? _clock.UtcNow : existing.FirstSeen)
            : existing.FirstDiscoveredUtc.Value;

        var incomingEffectiveDiscovered = (incoming.FirstDiscoveredUtc == null || incoming.FirstDiscoveredUtc == DateTime.MinValue)
            ? (incoming.FirstSeen == DateTime.MinValue ? _clock.UtcNow : incoming.FirstSeen)
            : incoming.FirstDiscoveredUtc.Value;

        existing.FirstDiscoveredUtc = incomingEffectiveDiscovered < existingEffectiveDiscovered 
            ? incomingEffectiveDiscovered 
            : existingEffectiveDiscovered;
        
        if (isUserAction)
        {
            existing.Location = incoming.Location;
            existing.Client = incoming.Client;
            existing.Building = incoming.Building;
            existing.Room = incoming.Room;
            existing.ManagedByUserId = incoming.ManagedByUserId;
            existing.ManagedByUsername = incoming.ManagedByUsername;

            existing.SerialNumber = incoming.SerialNumber;
            existing.AssetTag = incoming.AssetTag;
            existing.FirmwareVersion = incoming.FirmwareVersion;
            existing.WarrantyExpiresUtc = incoming.WarrantyExpiresUtc;
            existing.IsSerialNumberManuallySet = incoming.IsSerialNumberManuallySet;
            existing.IsFirmwareVersionManuallySet = incoming.IsFirmwareVersionManuallySet;
            existing.IsAssetTagManuallySet = incoming.IsAssetTagManuallySet;
            existing.IsWarrantyExpiresManuallySet = incoming.IsWarrantyExpiresManuallySet;
            existing.AssetMetadataSource = incoming.AssetMetadataSource;

            existing.AttachmentKind = string.IsNullOrWhiteSpace(incoming.AttachmentKind) ? "Unknown" : incoming.AttachmentKind;
            existing.AttachmentPort = string.IsNullOrWhiteSpace(incoming.AttachmentPort) ? null : incoming.AttachmentPort.Trim();
            existing.AttachmentParentDeviceId = incoming.AttachmentParentDeviceId;
            var parentMac = DeviceMacNormalizer.Normalize(incoming.AttachmentParentMac);
            existing.AttachmentParentMac = string.IsNullOrEmpty(parentMac) ? null : parentMac;
            existing.AttachmentParentIp = string.IsNullOrWhiteSpace(incoming.AttachmentParentIp)
                ? null
                : incoming.AttachmentParentIp.Trim();
            existing.IsAttachmentManuallySet = true;
        }
        else
        {
            existing.Location = incoming.Location ?? existing.Location;
            existing.Client = incoming.Client ?? existing.Client;
            existing.Building = incoming.Building ?? existing.Building;
            existing.Room = incoming.Room ?? existing.Room;
            existing.ManagedByUserId = incoming.ManagedByUserId ?? existing.ManagedByUserId;
            existing.ManagedByUsername = incoming.ManagedByUsername ?? existing.ManagedByUsername;

            if (!StacksAtlas.Core.State.ExecutionState.IsHub)
            {
                var settings = _settingsStore.Current;
                existing.Client ??= settings.Client;
                existing.Building ??= settings.Building;
                existing.Room ??= settings.Room;
                if (string.IsNullOrEmpty(existing.NodeId))
                {
                    existing.NodeId = settings.NodeId;
                }
            }
        }

        // Final Safety Check: Ensure Name is never null for database integrity
        if (string.IsNullOrWhiteSpace(existing.Name))
        {
            existing.Name = !string.IsNullOrWhiteSpace(existing.Hostname) ? existing.Hostname : existing.IpAddress;
        }

        ApplyDiscoveryProvenance(existing, incoming, isUserAction);

        if (isHub && !isUserAction)
            FederationDeviceIdentity.ApplyLifecycleFields(existing, incoming);

        return existing;
    }

    /// <summary>
    /// Hub mirrors Node-originated archive / remove-from-fleet / restore decisions via telemetry.
    /// </summary>
    private static void ApplyFederatedLifecycleState(Device existing, Device incoming) =>
        FederationDeviceIdentity.ApplyLifecycleFields(existing, incoming);

    /// <summary>
    /// Prepares a new device for insertion by running initial classification
    /// and setting timestamps so it appears identified from the first moment.
    /// </summary>
    public Device PrepareNewDevice(Device incoming)
    {
        incoming.FirstDiscoveredUtc = (incoming.FirstDiscoveredUtc == null || incoming.FirstDiscoveredUtc == DateTime.MinValue)
            ? (incoming.FirstSeen == DateTime.MinValue ? _clock.UtcNow : incoming.FirstSeen)
            : incoming.FirstDiscoveredUtc;

        incoming.FirstSeen = incoming.FirstSeen == DateTime.MinValue ? _clock.UtcNow : incoming.FirstSeen;
        incoming.LastSeen = incoming.LastSeen == DateTime.MinValue ? _clock.UtcNow : incoming.LastSeen;

        if (!StacksAtlas.Core.State.ExecutionState.IsHub)
        {
            var settings = _settingsStore.Current;
            incoming.Client ??= settings.Client;
            incoming.Building ??= settings.Building;
            incoming.Room ??= settings.Room;
            if (string.IsNullOrEmpty(incoming.NodeId))
            {
                incoming.NodeId = settings.NodeId;
            }
        }

        // Run classification so new devices don't appear as "Unknown" for an entire sweep cycle
        var hints = new List<ClassificationHint>();

        var nameHints = _intelligence.GetHints(incoming.Name, incoming.Hostname, null, incoming.MacAddress, null);
        hints.AddRange(nameHints);

        var portHints = _intelligence.GetHints(null, null, null, null, incoming.OpenPorts);
        hints.AddRange(portHints);

        var recogHints = _recogService.GetHints(incoming.HttpTitle, incoming.SnmpSysDescr);
        hints.AddRange(recogHints);

        var (ouiVendor, ouiType) = OuiDatabase.Lookup(incoming.MacAddress);
        if (ouiVendor != "Unknown Vendor")
        {
            hints.Add(new ClassificationHint(ouiType, ouiVendor, null, 95, "OUI"));
        }

        if (hints.Count > 0)
        {
            var (refinedType, refinedModel, refinedVendor, confidence, source) = _intelligence.Synthesize(hints);

            if (refinedType != DeviceType.Unknown && confidence >= 30)
            {
                incoming.Type = IntelligenceEngine.FormatType(refinedType.ToString());
                if (!string.IsNullOrWhiteSpace(refinedModel))
                    incoming.Model = refinedModel;
                
                incoming.IdentitySource = source;
                incoming.ConfidenceScore = confidence;
            }

            if (!string.IsNullOrWhiteSpace(refinedVendor))
                incoming.Vendor = refinedVendor;
        }

        // Final Safety Check: Ensure Name is never null for database integrity
        if (string.IsNullOrWhiteSpace(incoming.Name))
        {
            incoming.Name = !string.IsNullOrWhiteSpace(incoming.Hostname) ? incoming.Hostname : incoming.IpAddress;
        }

        return incoming;
    }

    private static void ApplyDiscoveryProvenance(Device existing, Device incoming, bool isUserAction)
    {
        if (existing.IsDiscoveryProvenanceManuallySet)
            return;

        if (isUserAction)
        {
            if (!string.IsNullOrWhiteSpace(incoming.DiscoveryScopeId) ||
                !string.IsNullOrWhiteSpace(incoming.DiscoveryInterfaceId) ||
                !string.IsNullOrWhiteSpace(incoming.DiscoveryInterfaceName) ||
                !string.IsNullOrWhiteSpace(incoming.DiscoveryVlanTag))
            {
                existing.DiscoveryScopeId = incoming.DiscoveryScopeId ?? existing.DiscoveryScopeId;
                existing.DiscoveryInterfaceId = incoming.DiscoveryInterfaceId ?? existing.DiscoveryInterfaceId;
                existing.DiscoveryInterfaceName = incoming.DiscoveryInterfaceName ?? existing.DiscoveryInterfaceName;
                existing.DiscoveryVlanTag = incoming.DiscoveryVlanTag ?? existing.DiscoveryVlanTag;
                existing.IsDiscoveryProvenanceManuallySet = true;
            }
            return;
        }

        if (string.IsNullOrWhiteSpace(existing.DiscoveryScopeId))
            existing.DiscoveryScopeId = incoming.DiscoveryScopeId ?? existing.DiscoveryScopeId;

        if (string.IsNullOrWhiteSpace(existing.DiscoveryInterfaceId))
            existing.DiscoveryInterfaceId = incoming.DiscoveryInterfaceId ?? existing.DiscoveryInterfaceId;

        if (string.IsNullOrWhiteSpace(existing.DiscoveryInterfaceName))
            existing.DiscoveryInterfaceName = incoming.DiscoveryInterfaceName ?? existing.DiscoveryInterfaceName;

        if (string.IsNullOrWhiteSpace(existing.DiscoveryVlanTag))
            existing.DiscoveryVlanTag = incoming.DiscoveryVlanTag ?? existing.DiscoveryVlanTag;
    }

    private static bool IsGenericName(string? name, string? ipAddress, string? hostname)
    {
        if (string.IsNullOrWhiteSpace(name)) return true;
        if (string.Equals(name, ipAddress, StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(name, hostname, StringComparison.OrdinalIgnoreCase)) return true;
        
        var lower = name.ToLowerInvariant();
        return lower.Contains(".hsd1") ||
               lower.Contains(".local") ||
               lower.Contains(".home") ||
               lower.Contains(".lan") ||
               lower.Contains(".internal") ||
               lower.Contains(".localdomain") ||
               lower.Contains(".arpa");
    }
}
