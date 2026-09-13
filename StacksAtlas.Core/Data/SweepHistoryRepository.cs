using System;
using System.Collections.Generic;
using System.Linq;
using LiteDB;
using Microsoft.Extensions.Logging;
using StacksAtlas.Core.Abstractions;
using StacksAtlas.Core.Models;

namespace StacksAtlas.Core.Data;

/// <summary>
/// Repository for managing discovery telemetry and sweep history in LiteDB.
/// </summary>
public class SweepHistoryRepository(LiteDatabase db, ILogger<SweepHistoryRepository> logger, IClock clock)
{
    private readonly ILiteCollection<SweepHistoryEntry> _collection = SetupCollection(db);

    private static ILiteCollection<SweepHistoryEntry> SetupCollection(LiteDatabase database)
    {
        var col = database.GetCollection<SweepHistoryEntry>("sweephistory");
        
        // Optimize performance for querying recent sweeps
        col.EnsureIndex(x => x.Start);
        col.EnsureIndex(x => x.CreatedAtUtc);
        
        return col;
    }

    public void Add(SweepHistoryEntry entry)
    {
        // Ensure the record has a consistent temporal source
        if (entry.CreatedAtUtc == default)
        {
            entry.CreatedAtUtc = clock.UtcNow;
        }

        _collection.Insert(entry);
    }

    /// <summary>
    /// BATCH OPTIMIZATION: Fetches the entire latest sweep and creates a lookup map.
    /// This prevents the N+1 performance bottleneck in the PingWorker.
    /// </summary>
    public Dictionary<string, HostScanResult> GetLatestHostMap()
    {
        try
        {
            var latest = GetLatest();

            if (latest == null) return [];

            // Transform the nested subnets/hosts into a flat Dictionary for O(1) lookups
            return latest.Subnets
                .SelectMany(s => s.Hosts)
                // We only care about hosts that actually have identification data
                .Where(h => !string.IsNullOrEmpty(h.Name) || !string.IsNullOrEmpty(h.Hostname))
                .GroupBy(h => h.Ip)
                .ToDictionary(
                    g => g.Key,
                    g => g.First()
                );
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to retrieve and map the latest sweep history.");
            return [];
        }
    }

    // Keep this for single-IP UI lookups, but avoid using it in loops!
    public HostScanResult? GetLatestResultForIp(string ip)
    {
        var latestSweep = GetLatest();
        if (latestSweep == null) return null;

        return latestSweep.Subnets
            .SelectMany(s => s.Hosts)
            .FirstOrDefault(h => h.Ip == ip);
    }

    public SweepHistoryEntry? GetLatest()
    {
        return _collection.Query()
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefault();
    }

    public IEnumerable<SweepHistoryEntry> GetRecent(int limit)
    {
        return _collection.Query()
            .OrderByDescending(x => x.CreatedAtUtc)
            .Limit(limit)
            .ToList();
    }

    public void PruneOlderThan(DateTime cutoffUtc)
    {
        _collection.DeleteMany(x => x.CreatedAtUtc < cutoffUtc);
    }
}
