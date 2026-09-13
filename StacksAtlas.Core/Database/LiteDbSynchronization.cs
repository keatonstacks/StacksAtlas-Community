namespace StacksAtlas.Core.Database;

/// <summary>
/// Provides a centralized synchronization root for LiteDB file-level operations.
/// </summary>
public static class LiteDbSynchronization
{
    /// <summary>
    /// Global lock for all LiteDB access in this process.
    /// </summary>
    public static readonly object SyncRoot = new();
}
