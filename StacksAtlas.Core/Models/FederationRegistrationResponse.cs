using System;

namespace StacksAtlas.Core.Models;

public class FederationRegistrationResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public DateTime LastKnownSyncUtc { get; set; }
    public bool SyncUsers { get; set; } // Legacy fallback
    public bool SyncUserRegistry { get; set; }
    public bool SyncSsoSettings { get; set; }
    public bool SyncAlertSettings { get; set; }
    public bool SyncSiemSettings { get; set; }
    public bool Decouple { get; set; }
    public bool DelegateAlertDispatch { get; set; }
    public string? InheritedLicensePayloadJson { get; set; }
    public string? InheritedLicenseSignature { get; set; }

    // Authoritative location metadata
    public string? Client { get; set; }
    public string? Building { get; set; }
    public string? Room { get; set; }
    public string? NodeDisplayName { get; set; }
}
