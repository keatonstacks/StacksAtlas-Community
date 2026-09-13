using System.Threading;
using System.Threading.Tasks;

namespace StacksAtlas.Core.Database;

/// <summary>
/// Defines the industrial-grade maintenance operations for the appliance database.
/// </summary>
public interface IDatabaseMaintenance
{
    /// <summary>
    /// Performs automated archival and purging of stale devices and events.
    /// </summary>
    Task<int> CleanupAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the physical size of the database file on disk in bytes.
    /// </summary>
    Task<long> GetDatabaseSizeBytesAsync();

    /// <summary>
    /// Reclaims disk space by rebuilding the database (Requires Exclusive Lock).
    /// </summary>
    Task CompactAsync(CancellationToken token = default);
}
