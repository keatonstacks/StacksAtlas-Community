using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using StacksAtlas.Core.Abstractions;
using StacksAtlas.Core.Models;

namespace StacksAtlas.Core.Data.Hub;

public class SqliteEventRepository(IDbContextFactory<HubDbContext> contextFactory, IClock clock) : IEventRepository
{
    private readonly IDbContextFactory<HubDbContext> _contextFactory = contextFactory;
    private readonly IClock _clock = clock;

    public void AddEvent(SystemEvent evt)
    {
        using var context = _contextFactory.CreateDbContext();
        if (evt.Timestamp == default)
        {
            evt.Timestamp = _clock.UtcNow;
        }

        var existing = context.SystemEvents.Find(evt.Id);
        if (existing != null)
        {
            context.Entry(existing).CurrentValues.SetValues(evt);
        }
        else
        {
            context.SystemEvents.Add(evt);
        }
        context.SaveChanges();
    }

    public IEnumerable<SystemEvent> GetRecent(int count = 20, string[]? nodeIds = null)
    {
        using var context = _contextFactory.CreateDbContext();
        IQueryable<SystemEvent> query = context.SystemEvents;
        if (nodeIds != null && nodeIds.Length > 0)
        {
            var filterIds = nodeIds.ToList();
            query = query.Where(x => filterIds.Contains(x.NodeId!));
        }
        return query.OrderByDescending(x => x.Timestamp).Take(count).ToList();
    }

    public void DeleteOlderThan(DateTime cutoff)
    {
        using var context = _contextFactory.CreateDbContext();
        var oldEvents = context.SystemEvents.Where(x => x.Timestamp < cutoff);
        context.SystemEvents.RemoveRange(oldEvents);
        context.SaveChanges();
    }

    public int ClearAll(string[]? nodeIds = null)
    {
        using var context = _contextFactory.CreateDbContext();
        IQueryable<SystemEvent> query = context.SystemEvents;
        if (nodeIds != null && nodeIds.Length > 0)
        {
            var filterIds = nodeIds.ToList();
            query = query.Where(x => filterIds.Contains(x.NodeId!));
        }
        var list = query.ToList();
        context.SystemEvents.RemoveRange(list);
        return context.SaveChanges();
    }

    public bool DeleteById(string id)
    {
        using var context = _contextFactory.CreateDbContext();
        if (TryParseObjectId(id, out var objectId))
        {
            var item = context.SystemEvents.Find(objectId);
            if (item != null)
            {
                context.SystemEvents.Remove(item);
                context.SaveChanges();
                return true;
            }
        }
        return false;
    }

    private static bool TryParseObjectId(string hex, out LiteDB.ObjectId result)
    {
        result = LiteDB.ObjectId.Empty;
        if (string.IsNullOrWhiteSpace(hex) || hex.Length != 24) return false;
        try
        {
            result = new LiteDB.ObjectId(hex);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
