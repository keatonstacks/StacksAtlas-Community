using System;
using System.Collections.Generic;

namespace StacksAtlas.Core.Models;

/// <summary>
/// Represents a historical record of an alert notification being processed or sent.
/// </summary>
public class AlertEvent
{
    public string Id { get; set; } = string.Empty;
    public DateTime TriggeredAt { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string DeviceIp { get; set; } = string.Empty;
    public AlertEventType AlertType { get; set; } = AlertEventType.Unknown;
    public List<string> SentToEmails { get; set; } = new();
    public List<string> SentToWebhooks { get; set; } = new();
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? NodeId { get; set; }
    public string? NodeName { get; set; }
    public string? Client { get; set; }
    public string? Building { get; set; }
    public string? Room { get; set; }
}
