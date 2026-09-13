using System;

namespace StacksAtlas.Core.Models;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Salt { get; set; } = string.Empty;
    public string Role { get; set; } = "Admin"; // Admin, Standard, Viewer, AlertOnly
    public string Provider { get; set; } = "Local"; // Local, OIDC, LDAP
    public string? ExternalId { get; set; } // OIDC 'sub' or LDAP 'distinguishedName'
    public string? OriginNodeId { get; set; } // Null if created on Hub or SSO, contains Node ID if migrated from edge
    public string? Email { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }

    // Alert Preferences
    public string? AlertEmail { get; set; }  // Can be different from login email
    public bool AlertsEnabled { get; set; } = false;
    public bool WebhookEnabled { get; set; } = false;
    public Guid? PreferredWebhookId { get; set; } // Legacy field for backwards compatibility if needed, but we use the list now
    public List<Guid> PreferredWebhookIds { get; set; } = new();
    
    public bool AlertOnDeviceDown { get; set; } = true;
    public bool AlertOnDeviceUp { get; set; } = false;
    public bool AlertOnNewDevice { get; set; } = true;
    public string AlertSeverity { get; set; } = "All"; // All, AssignedOnly
}
