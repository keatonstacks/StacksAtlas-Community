using System;
using System.Collections.Generic;

namespace StacksAtlas.Core.Models;

public class FederationCommand
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string CommandType { get; set; } = string.Empty; // "Ping", "WakeOnLan", "DeepScan"
    public string TargetId { get; set; } = string.Empty;   // Device ID (GUID)
    public Dictionary<string, string> Parameters { get; set; } = new();
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class FederationCommandResult
{
    public string CommandId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? Message { get; set; }
    public object? Data { get; set; }
}
