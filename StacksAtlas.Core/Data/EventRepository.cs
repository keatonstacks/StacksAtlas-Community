using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using LiteDB;
using Microsoft.Extensions.Logging;
using StacksAtlas.Core.Abstractions;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Services.Security;

namespace StacksAtlas.Core.Data;

/// <summary>
/// Repository for managing System Events.
/// </summary>
public interface IEventRepository
{
    void AddEvent(SystemEvent evt);
    IEnumerable<SystemEvent> GetRecent(int count = 20, string[]? nodeIds = null);
    void DeleteOlderThan(DateTime cutoff);
    int ClearAll(string[]? nodeIds = null);
    bool DeleteById(string id);
}

/// <summary>
/// Repository for managing System Events in LiteDB.
/// </summary>
public class EventRepository(LiteDatabase db, DatabaseSecurityService securityService, ILogger<EventRepository> logger, IClock clock) : IEventRepository
{
    private readonly ILiteCollection<SystemEvent> _collection = SetupCollection(db);

    private static ILiteCollection<SystemEvent> SetupCollection(LiteDatabase database)
    {
        var col = database.GetCollection<SystemEvent>("events");
        // Ensure index to prevent full collection scans on Timestamp queries
        col.EnsureIndex(x => x.Timestamp);
        return col;
    }

    // -------------------------------------------------------------
    //  Add event
    // -------------------------------------------------------------
    public void AddEvent(SystemEvent evt)
    {
        // Ensure timestamp is always set
        if (evt.Timestamp == default)
        {
            evt.Timestamp = clock.UtcNow;
        }

        _collection.Insert(evt);

        if (StacksAtlas.Core.State.ExecutionState.IsFederationActive)
        {
            var pendingCol = db.GetCollection<PendingEvent>("pending_events");
            pendingCol.Insert(new PendingEvent
            {
                Event = evt,
                CreatedAtUtc = clock.UtcNow
            });
        }
    }

    // -------------------------------------------------------------
    //  Get recent events
    // -------------------------------------------------------------
    public IEnumerable<SystemEvent> GetRecent(int count = 20, string[]? nodeIds = null)
    {
        var query = _collection.Query();
        if (nodeIds != null && nodeIds.Length > 0)
        {
            // Note: LiteDB 'In' query works cleanly with standard arrays
            query = query.Where(x => nodeIds.Contains(x.NodeId));
        }
        
        return query.OrderByDescending(x => x.Timestamp)
                    .Limit(count)
                    .ToList();
    }

    // -------------------------------------------------------------
    //  Delete older events & Rebuild Optimization
    // -------------------------------------------------------------
    public void DeleteOlderThan(DateTime cutoff)
    {
        int deletedCount = _collection.DeleteMany(LiteDB.Query.LT("Timestamp", new BsonValue(cutoff)));
        
        // Database Shrinking mechanism
        if (deletedCount > 1000)
        {
            logger.LogInformation("Deleted {Count} old events. Triggering database rebuild to reclaim disk space.", deletedCount);
            try
            {
                // Rebuild compacts the database and re-indexes. It requires an exclusive lock.
                // We wrap it in a try-catch to prevent crashing the worker if another thread holds a lock.
                var dbPassword = securityService.GetDatabasePassword();
                var options = !string.IsNullOrEmpty(dbPassword) 
                    ? new LiteDB.Engine.RebuildOptions { Password = dbPassword } 
                    : null;
                db.Rebuild(options);
                logger.LogInformation("Database rebuild completed successfully.");
            }
            catch (LiteException ex) when (ex.ErrorCode == LiteException.LOCK_TIMEOUT)
            {
                logger.LogWarning("Database rebuild skipped due to lock contention. Will retry on next massive deletion.");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Database rebuild failed.");
            }
        }
    }

    // -------------------------------------------------------------
    //  Delete all events
    // -------------------------------------------------------------
    public int ClearAll(string[]? nodeIds = null)
    {
        int count;
        if (nodeIds == null || nodeIds.Length == 0)
        {
            count = _collection.DeleteAll();
        }
        else
        {
            count = _collection.DeleteMany(x => nodeIds.Contains(x.NodeId));
        }

        if (count > 0)
        {
            try
            {
                var dbPassword = securityService.GetDatabasePassword();
                var options = !string.IsNullOrEmpty(dbPassword) 
                    ? new LiteDB.Engine.RebuildOptions { Password = dbPassword } 
                    : null;
                db.Rebuild(options);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to rebuild database after clearing events.");
            }
        }
        return count;
    }

    // -------------------------------------------------------------
    //  Delete by ID
    // -------------------------------------------------------------
    public bool DeleteById(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;

        if (TryParseObjectId(id, out var objectId))
        {
            try
            {
                return _collection.Delete(objectId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "EventRepo: Failed to delete ObjectId {ObjectId} (Original input: {Id})", objectId, id);
                return false;
            }
        }

        return false;
    }

    /// <summary>
    /// Robust parsing utility to handle exact Hex strings, synthetic IDs, and JSON-wrapped $oid objects.
    /// Does not use brittle string manipulation.
    /// </summary>
    private bool TryParseObjectId(string id, out ObjectId objectId)
    {
        objectId = ObjectId.Empty;
        string targetId = id.Trim();

        // 1. Literal guard against React frontend bug where an object is serialized as string
        if (targetId == "[object Object]")
        {
            logger.LogWarning("EventRepository received a literal '[object Object]' for deletion. This indicates a frontend bug in ApiService.ts where the ID is not properly extracted from the payload.");
            return false;
        }

        // 2. Standard 24-char hex
        if (IsValidObjectId(targetId, out objectId))
        {
            return true;
        }

        // 3. Robust parsing of JSON Object wrapper: {"$oid":"..."}
        if (targetId.StartsWith('{') && targetId.EndsWith('}'))
        {
            logger.LogWarning("EventRepository received a JSON-wrapped ID '{Id}'. The frontend should extract the string ID before transmitting the DELETE request.", targetId);
            try
            {
                using var doc = JsonDocument.Parse(targetId);
                if (doc.RootElement.TryGetProperty("$oid", out var oidElement) && oidElement.ValueKind == JsonValueKind.String)
                {
                    string? innerId = oidElement.GetString();
                    if (!string.IsNullOrWhiteSpace(innerId) && IsValidObjectId(innerId, out objectId))
                    {
                        return true;
                    }
                }
            }
            catch (JsonException ex)
            {
                logger.LogError(ex, "EventRepository: Failed to parse JSON-wrapped ObjectId payload: {Id}", targetId);
            }
            
            return false;
        }

        // 4. LiteDB Synthetic / Composite ObjectId (timestamp-machine-pid-increment)
        if (targetId.Contains('-'))
        {
            var parts = targetId.Split('-');
            if (parts.Length == 4)
            {
                try
                {
                    int timestamp = int.Parse(parts[0]);
                    int machine = int.Parse(parts[1]);
                    short pid = short.Parse(parts[2]);
                    int increment = int.Parse(parts[3]);

                    objectId = new ObjectId(timestamp, machine, pid, increment);
                    return true;
                }
                catch
                {
                    logger.LogWarning("EventRepository: Failed to parse hyphenated synthetic ObjectId format: {Id}", targetId);
                }
            }
        }

        logger.LogError("EventRepository: Unrecognized ObjectId format '{Id}' sent for deletion.", targetId);
        return false;
    }

    private static bool IsValidObjectId(string? hex, out ObjectId result)
    {
        result = ObjectId.Empty;
        if (string.IsNullOrWhiteSpace(hex) || hex.Length != 24) return false;

        try
        {
            result = new ObjectId(hex);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
