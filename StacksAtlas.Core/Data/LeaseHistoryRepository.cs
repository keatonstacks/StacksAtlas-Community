using LiteDB;
using Microsoft.Extensions.Logging;
using StacksAtlas.Core.Abstractions;
using StacksAtlas.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace StacksAtlas.Core.Data;

public class LeaseHistoryRepository(ILiteDatabase db, ILogger<LeaseHistoryRepository> logger, IClock clock)
{
    private const string CollectionName = "lease_history";
    private readonly ILiteCollection<LeaseHistory> _collection = SetupCollection(db);

    private static ILiteCollection<LeaseHistory> SetupCollection(ILiteDatabase database)
    {
        var col = database.GetCollection<LeaseHistory>(CollectionName);
        
        // Ensure indices for rapid lookup by MAC or IP
        col.EnsureIndex(x => x.MacAddress);
        col.EnsureIndex(x => x.IpAddress);
        return col;
    }

    public void UpsertLease(string mac, string ip, string? hostname, string? vendor)
    {
        if (string.IsNullOrWhiteSpace(mac) || string.IsNullOrWhiteSpace(ip))
            return;

        // Normalization: Ensure MAC is uppercase and consistent
        var cleanMac = mac.Replace(":", "").Replace("-", "").ToUpperInvariant();

        try
        {
            var existing = _collection.Query()
                .Where(x => x.MacAddress == cleanMac && x.IpAddress == ip)
                .OrderByDescending(x => x.LastSeen)
                .FirstOrDefault();

            if (existing != null)
            {
                existing.LastSeen = clock.UtcNow;
                if (!string.IsNullOrWhiteSpace(hostname)) existing.Hostname = hostname;
                if (!string.IsNullOrWhiteSpace(vendor)) existing.Vendor = vendor;
                _collection.Update(existing);
            }
            else
            {
                var newLease = new LeaseHistory
                {
                    MacAddress = cleanMac,
                    IpAddress = ip,
                    Hostname = hostname,
                    Vendor = vendor,
                    FirstSeen = clock.UtcNow,
                    LastSeen = clock.UtcNow
                };
                _collection.Insert(newLease);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to upsert lease history for {Mac} -> {Ip}", mac, ip);
        }
    }

    public List<LeaseHistory> GetHistoryForMac(string mac)
    {
        return _collection
            .Find(x => x.MacAddress == mac)
            .OrderByDescending(x => x.LastSeen)
            .ToList();
    }

    public List<LeaseHistory> GetHistoryForIp(string ip)
    {
        return _collection
            .Find(x => x.IpAddress == ip)
            .OrderByDescending(x => x.LastSeen)
            .ToList();
    }
}
