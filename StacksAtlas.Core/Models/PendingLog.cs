using System;
using LiteDB;

namespace StacksAtlas.Core.Models;

/// <summary>
/// Database-persisted container for logs queued on the Node waiting to be replayed to the Hub.
/// </summary>
public class PendingLog
{
    [BsonId]
    public Guid Id { get; set; } = Guid.NewGuid();
    public string LogLevel { get; set; } = "Information";
    public string Message { get; set; } = string.Empty;
    public string? Exception { get; set; }
    public DateTime Timestamp { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
