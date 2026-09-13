namespace StacksAtlas.Core.Models;

public enum WebhookProvider
{
    Generic,
    Slack,
    Teams,
    Discord
}

public enum WebhookStatus
{
    Active,
    CircuitOpen, // Paused due to repeated failures
    Disabled
}

public class WebhookConfiguration
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty; // Will be encrypted-at-rest
    public WebhookProvider Provider { get; set; } = WebhookProvider.Generic;
    public bool Enabled { get; set; } = true;
    public string? SigningSecret { get; set; } = string.Empty; // Optional HMAC secret
    
    // Event Filtering
    public List<AlertEventType> TriggerEvents { get; set; } = new() 
    { 
        AlertEventType.DeviceDown, 
        AlertEventType.DeviceUp 
    };
    // Circuit Breaker State
    public WebhookStatus Status { get; set; } = WebhookStatus.Active;
    public int FailureCount { get; set; } = 0;
    public DateTime? LastFailureAt { get; set; }
    public DateTime? CircuitResetAt { get; set; }

    // Scoping & Consistency (Matching User Alert Profile)
    public string AlertSeverity { get; set; } = "All"; // All, AssignedOnly
    public Guid? AssignedToUserId { get; set; } // Only fire for devices managed by this user if AssignedOnly is selected

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastUpdatedAt { get; set; }

    public string? LastErrorMessage { get; set; }
}
