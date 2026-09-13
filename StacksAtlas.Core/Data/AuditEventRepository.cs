using LiteDB;
using StacksAtlas.Core.Abstractions;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Services.Audit;
using StacksAtlas.Core.State;

namespace StacksAtlas.Core.Data;

public class AuditEventRepository(LiteDatabase db, IClock clock) : IAuditEventRepository
{
    private const string CollectionName = "audit_events";
    private const string PendingCollectionName = "pending_audit_events";

    private readonly LiteDatabase _db = db;
    private readonly ILiteCollection<AuditEvent> _collection = SetupCollection(db);
    private readonly IClock _clock = clock;

    private static ILiteCollection<AuditEvent> SetupCollection(LiteDatabase database)
    {
        var col = database.GetCollection<AuditEvent>(CollectionName);
        col.EnsureIndex(x => x.TimestampUtc);
        col.EnsureIndex(x => x.Action);
        col.EnsureIndex(x => x.NodeId);
        return col;
    }

    public void Append(AuditEvent auditEvent)
    {
        AuditEventNormalizer.Normalize(auditEvent, _clock.UtcNow);

        var existing = _collection.FindById(auditEvent.Id);
        if (existing != null)
            _collection.Update(auditEvent);
        else
            _collection.Insert(auditEvent);

        if (ExecutionState.IsFederationActive)
        {
            var pendingCol = _db.GetCollection<PendingAuditEvent>(PendingCollectionName);
            pendingCol.Insert(new PendingAuditEvent
            {
                Event = auditEvent,
                CreatedAtUtc = _clock.UtcNow
            });
        }
    }

    public IReadOnlyList<AuditEvent> Query(
        int skip,
        int take,
        string? actionPrefix,
        string[]? nodeIds,
        DateTime? sinceUtc = null,
        DateTime? untilUtc = null,
        bool hubOnly = false)
    {
        var limit = Math.Clamp(take, 1, 500);
        var offset = Math.Max(0, skip);

        IEnumerable<AuditEvent> query = _collection.Query()
            .OrderByDescending(x => x.TimestampUtc)
            .ToEnumerable();

        if (sinceUtc.HasValue)
            query = query.Where(x => x.TimestampUtc >= sinceUtc.Value);

        if (untilUtc.HasValue)
            query = query.Where(x => x.TimestampUtc <= untilUtc.Value);

        if (hubOnly)
            query = query.Where(x => string.IsNullOrEmpty(x.NodeId));

        if (!string.IsNullOrWhiteSpace(actionPrefix))
        {
            var prefix = actionPrefix.Trim();
            query = query.Where(x => x.Action.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }

        if (!hubOnly && nodeIds is { Length: > 0 })
        {
            var filter = nodeIds.Where(id => !string.IsNullOrWhiteSpace(id)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (filter.Count > 0)
                query = query.Where(x => x.NodeId != null && filter.Contains(x.NodeId));
        }

        return query.Skip(offset).Take(limit).ToList();
    }
}
