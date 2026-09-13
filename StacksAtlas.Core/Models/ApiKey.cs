using System;

namespace StacksAtlas.Core.Models;

/// <summary>
/// Represents a secure API key used for external integrations.
/// </summary>
public class ApiKey
{
    public Guid Id { get; set; }
    
    // The user this key belongs to
    public Guid UserId { get; set; }
    
    // Friendly name (e.g., "Home Assistant Integration")
    public string Label { get; set; } = string.Empty;
    
    // We NEVER store the raw key. We only store a secure hash.
    public string KeyHash { get; set; } = string.Empty;
    
    // The salt used to hash this specific key.
    public string Salt { get; set; } = string.Empty;
    
    // A safe-to-display prefix to help users identify the key (e.g., "sa_abc...")
    public string KeyPrefix { get; set; } = string.Empty;
    
    public DateTime CreatedAt { get; set; }
    
    public DateTime? LastUsedAt { get; set; }
    
    public bool IsActive { get; set; } = true;
}
