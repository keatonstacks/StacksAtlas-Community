using System.Collections.Concurrent;

namespace StacksAtlas.API.Hubs;

/// <summary>
/// In-memory SignalR connection registry for federated nodes (Hub only).
/// </summary>
internal static class FederationSignalRRegistry
{
    private static readonly ConcurrentDictionary<string, string> ActiveConnections = new();

    public static void Register(string connectionId, string nodeId) =>
        ActiveConnections[connectionId] = nodeId;

    public static bool TryUnregister(string connectionId, out string? nodeId) =>
        ActiveConnections.TryRemove(connectionId, out nodeId);

    public static bool TryGetNodeId(string connectionId, out string nodeId) =>
        ActiveConnections.TryGetValue(connectionId, out nodeId!);

    public static void EvictConnectionsForNode(string nodeId, string? exceptConnectionId = null)
    {
        foreach (var entry in ActiveConnections.Where(kvp =>
                     string.Equals(kvp.Value, nodeId, StringComparison.OrdinalIgnoreCase)
                     && !string.Equals(kvp.Key, exceptConnectionId, StringComparison.Ordinal)).ToList())
        {
            ActiveConnections.TryRemove(entry.Key, out _);
        }
    }

    public static bool IsNodeConnected(string nodeId, string? connectionId)
    {
        if (string.IsNullOrEmpty(connectionId))
            return false;

        return ActiveConnections.TryGetValue(connectionId, out var activeNodeId)
               && string.Equals(activeNodeId, nodeId, StringComparison.OrdinalIgnoreCase);
    }
}
