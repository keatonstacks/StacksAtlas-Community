using StacksAtlas.Core.Helpers;
using StacksAtlas.Core.Models;

namespace StacksAtlas.Core.Services.Audit;

/// <summary>
/// Ensures audit rows always satisfy required columns (anonymous actors use empty strings, not null).
/// Critical for Hub SQLite NOT NULL constraints and SignalR/LiteDB round-trips.
/// </summary>
public static class AuditEventNormalizer
{
    public static void Normalize(AuditEvent auditEvent, DateTime? fallbackTimestampUtc = null)
    {
        if (string.IsNullOrWhiteSpace(auditEvent.Id))
            auditEvent.Id = Guid.NewGuid().ToString("N");

        if (auditEvent.TimestampUtc == default)
            auditEvent.TimestampUtc = fallbackTimestampUtc ?? DateTime.UtcNow;

        auditEvent.Action = auditEvent.Action?.Trim() ?? string.Empty;
        auditEvent.ActorUserId = auditEvent.ActorUserId ?? string.Empty;
        auditEvent.ActorUsername = string.IsNullOrWhiteSpace(auditEvent.ActorUsername)
            ? "unknown"
            : auditEvent.ActorUsername.Trim();
        auditEvent.ActorRole = auditEvent.ActorRole ?? string.Empty;
        auditEvent.ResourceType = auditEvent.ResourceType ?? string.Empty;
        auditEvent.Outcome = string.IsNullOrWhiteSpace(auditEvent.Outcome)
            ? AuditOutcomes.Success
            : auditEvent.Outcome.Trim();

        if (auditEvent.ResourceId is { Length: 0 })
            auditEvent.ResourceId = null;
        if (auditEvent.ClientIp is { Length: > 0 })
            auditEvent.ClientIp = NetworkHelper.NormalizeIpAddress(auditEvent.ClientIp);
        else
            auditEvent.ClientIp = null;
        if (auditEvent.Detail is { Length: 0 })
            auditEvent.Detail = null;
        if (auditEvent.NodeId is { Length: 0 })
            auditEvent.NodeId = null;
        if (auditEvent.NodeName is { Length: 0 })
            auditEvent.NodeName = null;
    }
}
