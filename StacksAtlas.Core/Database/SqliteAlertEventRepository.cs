using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using StacksAtlas.Core.Abstractions;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Data.Hub;

namespace StacksAtlas.Core.Database;

public class SqliteAlertEventRepository(IDbContextFactory<HubDbContext> contextFactory, IClock clock) : IAlertEventRepository
{
    private readonly IDbContextFactory<HubDbContext> _contextFactory = contextFactory;
    private readonly IClock _clock = clock;

    public void LogAlert(AlertEvent alertEvent, Device? device = null, bool isDelegation = false)
    {
        using var context = _contextFactory.CreateDbContext();
        if (alertEvent.TriggeredAt == default)
        {
            alertEvent.TriggeredAt = _clock.UtcNow;
        }

        var existing = context.AlertEvents.Find(alertEvent.Id);
        if (existing != null)
        {
            context.Entry(existing).CurrentValues.SetValues(alertEvent);
        }
        else
        {
            context.AlertEvents.Add(alertEvent);
        }
        context.SaveChanges();
    }

    public IEnumerable<AlertEvent> GetRecentAlerts(int limit = 50, string[]? nodeIds = null)
    {
        using var context = _contextFactory.CreateDbContext();
        IQueryable<AlertEvent> query = context.AlertEvents;
        if (nodeIds != null && nodeIds.Length > 0)
        {
            var filterIds = nodeIds.ToList();
            query = query.Where(x => filterIds.Contains(x.NodeId!));
        }
        return query.OrderByDescending(x => x.TriggeredAt).Take(limit).ToList();
    }

    public List<AlertEvent> GetAlertsByUser(string email, int limit = 50)
    {
        using var context = _contextFactory.CreateDbContext();
        return context.AlertEvents
            .AsEnumerable()
            .Where(x => x.SentToEmails.Contains(email))
            .OrderByDescending(x => x.TriggeredAt)
            .Take(limit)
            .ToList();
    }

    public bool DeleteAlert(string id)
    {
        using var context = _contextFactory.CreateDbContext();
        var item = context.AlertEvents.Find(id);
        if (item != null)
        {
            context.AlertEvents.Remove(item);
            context.SaveChanges();
            return true;
        }
        return false;
    }

    public int ClearHistory(string[]? nodeIds = null)
    {
        using var context = _contextFactory.CreateDbContext();
        IQueryable<AlertEvent> query = context.AlertEvents;
        if (nodeIds != null && nodeIds.Length > 0)
        {
            var filterIds = nodeIds.ToList();
            query = query.Where(x => filterIds.Contains(x.NodeId!));
        }
        var list = query.ToList();
        context.AlertEvents.RemoveRange(list);
        return context.SaveChanges();
    }
}
