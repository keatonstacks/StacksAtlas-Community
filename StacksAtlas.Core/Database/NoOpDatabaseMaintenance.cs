using System.Threading;
using System.Threading.Tasks;

namespace StacksAtlas.Core.Database;

public class NoOpDatabaseMaintenance : IDatabaseMaintenance
{
    public Task<int> CleanupAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
    public Task<long> GetDatabaseSizeBytesAsync() => Task.FromResult(0L);
    public Task CompactAsync(CancellationToken token = default) => Task.CompletedTask;
}
