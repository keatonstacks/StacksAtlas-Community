using LiteDB;

namespace StacksAtlas.Core.Models;

/// <summary>
/// Append-only admin audit record (human actions on the appliance).
/// Separate from <see cref="SystemEvent"/> (network/discovery) and Serilog (engine noise).
/// </summary>
public class AuditEvent
{
    [BsonId]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Namespaced action, e.g. auth.login.success, user.create.</summary>
    public string Action { get; set; } = string.Empty;

    public string ActorUserId { get; set; } = string.Empty;
    public string ActorUsername { get; set; } = string.Empty;
    public string ActorRole { get; set; } = string.Empty;

    public string ResourceType { get; set; } = string.Empty;
    public string? ResourceId { get; set; }

    /// <summary>Success, Denied, or Failed.</summary>
    public string Outcome { get; set; } = "Success";

    public string? ClientIp { get; set; }

    /// <summary>Human-readable summary  -  never secrets.</summary>
    public string? Detail { get; set; }

    public string? NodeId { get; set; }
    public string? NodeName { get; set; }
}
