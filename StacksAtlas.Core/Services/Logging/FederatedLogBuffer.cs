using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using StacksAtlas.Core.Models;

namespace StacksAtlas.Core.Services.Logging;

/// <summary>
/// A thread-safe, bounded in-memory buffer to hold logs on the Node until they are streamed to the Hub.
/// </summary>
public class FederatedLogBuffer
{
    private readonly ConcurrentQueue<FederatedLog> _queue = new();
    private const int MaxBufferSize = 1000;

    public void Enqueue(FederatedLog log)
    {
        _queue.Enqueue(log);
        
        // Prevent memory leaks by dropping oldest logs if we exceed limit
        while (_queue.Count > MaxBufferSize)
        {
            _queue.TryDequeue(out _);
        }
    }

    public List<FederatedLog> Drain()
    {
        var logs = new List<FederatedLog>();
        while (_queue.TryDequeue(out var log))
        {
            logs.Add(log);
        }
        return logs;
    }
}
