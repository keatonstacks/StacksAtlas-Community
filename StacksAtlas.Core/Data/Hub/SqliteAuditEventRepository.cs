using Microsoft.EntityFrameworkCore;
using StacksAtlas.Core.Abstractions;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Services.Audit;

namespace StacksAtlas.Core.Data.Hub;

public class SqliteAuditEventRepository(IDbContextFactory<HubDbContext> contextFactory, IClock clock) : IAuditEventRepository
{
    private readonly IDbContextFactory<HubDbContext> _contextFactory = contextFactory;
    private readonly IClock _clock = clock;

    public void Append(AuditEvent auditEvent)
    {
        AuditEventNormalizer.Normalize(auditEvent, _clock.UtcNow);

        using var context = _contextFactory.CreateDbContext();
        var existing = context.AuditEvents.Find(auditEvent.Id);
        if (existing != null)
            context.Entry(existing).CurrentValues.SetValues(auditEvent);
        else
            context.AuditEvents.Add(auditEvent);

        context.SaveChanges();
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
        using var context = _contextFactory.CreateDbContext();
        var limit = Math.Clamp(take, 1, 500);
        var offset = Math.Max(0, skip);

        IQueryable<AuditEvent> query = context.AuditEvents.AsNoTracking();

        if (sinceUtc.HasValue)
            query = query.Where(x => x.TimestampUtc >= sinceUtc.Value);

        if (untilUtc.HasValue)
            query = query.Where(x => x.TimestampUtc <= untilUtc.Value);

        if (hubOnly)
            query = query.Where(x => x.NodeId == null || x.NodeId == "");

        if (!string.IsNullOrWhiteSpace(actionPrefix))
        {
            var prefix = actionPrefix.Trim();
            query = query.Where(x => x.Action.StartsWith(prefix));
        }

        if (!hubOnly && nodeIds is { Length: > 0 })
        {
            var filter = nodeIds.Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
            if (filter.Count > 0)
                query = query.Where(x => x.NodeId != null && filter.Contains(x.NodeId));
        }

        return query
            .OrderByDescending(x => x.TimestampUtc)
            .Skip(offset)
            .Take(limit)
            .ToList();
    }
}
