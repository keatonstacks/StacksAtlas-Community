using System;

namespace StacksAtlas.Core.Models;

/// <summary>
/// Relational model for logs streamed from federated remote nodes back to the Hub.
/// </summary>
public class FederatedLog
{
    public long Id { get; set; }
    public string NodeId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string LogLevel { get; set; } = "Information";
    public string Message { get; set; } = string.Empty;
    public string? Exception { get; set; }
}
