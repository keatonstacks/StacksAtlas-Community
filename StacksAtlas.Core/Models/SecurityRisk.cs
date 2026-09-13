using System;

namespace StacksAtlas.Core.Models;

public class SecurityRisk
{
    public string DeviceName { get; set; } = string.Empty;
    /// <summary>Stable SA-001::Severity::... storage key (ignore/ack APIs).</summary>
    public string Reason { get; set; } = string.Empty;
    /// <summary>Report rollup: Critical or Warning.</summary>
    public string Level { get; set; } = "Warning";
    public string? DeviceId { get; set; }

    public string? FindingId { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Mitigation { get; set; }
    /// <summary>Finding-level severity (Critical, High, Medium, …).</summary>
    public string? Severity { get; set; }
}
