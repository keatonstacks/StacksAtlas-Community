using StacksAtlas.Core.Models;
using StacksAtlas.Core.Settings;
using System.Diagnostics;

namespace StacksAtlas.Core.Services.Scanning
{
    public interface ISweepMetricsCollector
    {
        Task<SweepResult> RunSweepAsync(CancellationToken token);
    }

    public sealed class SweepMetricsCollector(SubnetScanner scanner, NetworkSettingsStore settingsStore, StacksAtlas.Core.Abstractions.IClock clock) : ISweepMetricsCollector
    {
        private readonly SubnetScanner _scanner = scanner;
        private readonly NetworkSettingsStore _settingsStore = settingsStore;
        private readonly StacksAtlas.Core.Abstractions.IClock _clock = clock;

        public async Task<SweepResult> RunSweepAsync(CancellationToken token)
        {
            var settings = _settingsStore.Load();

            var sweepStart = _clock.UtcNow;
            var sw = Stopwatch.StartNew();

            // 1. Create the tasks
            var scanTasks = settings.Subnets.Select(scope => _scanner.ScanAsync(scope, token)).ToList();

            try
            {
                // 2. FIX: Wrap Task.WhenAll with WaitAsync(token) 
                // This forces the await to break the second the token is cancelled
                var resultsArray = await Task.WhenAll(scanTasks).WaitAsync(token);
                var subnetResults = resultsArray.ToList();

                sw.Stop();
                var sweepEnd = _clock.UtcNow;

                return new SweepResult(
                    Start: sweepStart,
                    End: sweepEnd,
                    DurationMs: sw.ElapsedMilliseconds,
                    TotalOnline: subnetResults.Sum(s => s.OnlineHosts),
                    TotalHosts: subnetResults.Sum(s => s.TotalHosts),
                    Subnets: subnetResults
                );
            }
            catch (OperationCanceledException)
            {
                // Log it if you want, but re-throwing is what makes the worker stop fast
                throw;
            }
        }
    }
}
