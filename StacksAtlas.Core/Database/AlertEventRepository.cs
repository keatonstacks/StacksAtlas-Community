using System;
using System.Collections.Generic;
using System.Linq;
using LiteDB;
using Microsoft.Extensions.Logging;
using StacksAtlas.Core.Abstractions;
using StacksAtlas.Core.Models;

namespace StacksAtlas.Core.Database;

public class AlertEventRepository(LiteDatabase db, ILogger<AlertEventRepository> logger, IClock clock) : IAlertEventRepository
{
    private const string CollectionName = "alert_events";
    private readonly ILiteCollection<AlertEvent> _collection = SetupCollection(db);

    private static ILiteCollection<AlertEvent> SetupCollection(LiteDatabase database)
    {
        var col = database.GetCollection<AlertEvent>(CollectionName);
        // Optimize for sorting by TriggeredAt
        col.EnsureIndex(x => x.TriggeredAt);
        return col;
    }

    public void LogAlert(AlertEvent alertEvent, Device? device = null, bool isDelegation = false)
    {
        try
        {
            if (alertEvent.TriggeredAt == default)
            {
                alertEvent.TriggeredAt = clock.UtcNow;
            }
            _collection.Insert(alertEvent);

            if (StacksAtlas.Core.State.ExecutionState.IsFederationActive)
            {
                var pendingCol = db.GetCollection<PendingAlert>("pending_alerts");
                pendingCol.Insert(new PendingAlert
                {
                    Alert = alertEvent,
                    Device = device,
                    IsDelegation = isDelegation,
                    CreatedAtUtc = clock.UtcNow
                });
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to log AlertEvent of type {AlertType}", alertEvent.AlertType);
        }
    }

    public IEnumerable<AlertEvent> GetRecentAlerts(int limit = 50, string[]? nodeIds = null)
    {
        try
        {
            var query = _collection.Query();
            if (nodeIds != null && nodeIds.Length > 0)
            {
                query = query.Where(x => nodeIds.Contains(x.NodeId));
            }
            return query.OrderByDescending(x => x.TriggeredAt).Limit(limit).ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get recent alerts");
            return Enumerable.Empty<AlertEvent>();
        }
    }

    public List<AlertEvent> GetAlertsByUser(string email, int limit = 50)
    {
        return _collection.Query()
            .Where(x => x.SentToEmails.Contains(email))
            .OrderByDescending(x => x.TriggeredAt)
            .Limit(limit)
            .ToList();
    }

    public bool DeleteAlert(string id)
    {
        try
        {
            return _collection.Delete(id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete AlertEvent with ID {Id}", id);
            return false;
        }
    }

    public int ClearHistory(string[]? nodeIds = null)
    {
        try
        {
            if (nodeIds == null || nodeIds.Length == 0)
            {
                return _collection.DeleteAll();
            }
            return _collection.DeleteMany(x => nodeIds.Contains(x.NodeId));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to clear alert history");
            return 0;
        }
    }
}
