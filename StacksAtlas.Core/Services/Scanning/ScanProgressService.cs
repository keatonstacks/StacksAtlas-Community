
namespace StacksAtlas.Core.Services.Scanning;

public record ScanProgressUpdate(int Percentage, string Phase, string Status, bool IsScanning);

public class ScanProgressService
{
    private long _processedCount;
    private long _totalCount;
    private string? _currentPhase = "Idle";
    private string? _statusMessage = "Ready";
    private int _isScanning = 0; // 0 = false, 1 = true

    public event Action<ScanProgressUpdate>? OnProgressUpdated;

    public int ProgressPercentage 
    {
        get 
        {
            long total = Interlocked.Read(ref _totalCount);
            if (total == 0) return 0;
            
            long processed = Interlocked.Read(ref _processedCount);
            double pct = (double)processed / total * 100.0;
            return Math.Min(100, (int)pct);
        }
    }
    
    public bool IsScanning => Interlocked.CompareExchange(ref _isScanning, 0, 0) == 1;
    public string CurrentPhase => Interlocked.CompareExchange(ref _currentPhase, null, null) ?? "Idle";
    public string StatusMessage => Interlocked.CompareExchange(ref _statusMessage, null, null) ?? "Ready";

    public void StartScan(long totalItems, string phase = "Scanning")
    {
        Interlocked.Exchange(ref _totalCount, totalItems);
        Interlocked.Exchange(ref _processedCount, 0);
        Interlocked.Exchange(ref _currentPhase, phase);
        Interlocked.Exchange(ref _statusMessage, $"Starting {phase}...");
        Interlocked.Exchange(ref _isScanning, 1);
        
        NotifyUpdate();
    }

    public void UpdateStatus(string message, string? phase = null)
    {
        Interlocked.Exchange(ref _statusMessage, message);
        if (phase != null) Interlocked.Exchange(ref _currentPhase, phase);
        NotifyUpdate();
    }

    public void ReportProgress(int batchSize)
    {
        Interlocked.Add(ref _processedCount, batchSize);
        
        // Basic Throttle: Only notify on significant progress or periodically
        // For now, we notify on every report as the scanner is already batching via Parallel.ForEach
        NotifyUpdate();
    }

    public void CompleteScan()
    {
        Interlocked.Exchange(ref _processedCount, Interlocked.Read(ref _totalCount));
        Interlocked.Exchange(ref _isScanning, 0);
        Interlocked.Exchange(ref _currentPhase, "Idle");
        Interlocked.Exchange(ref _statusMessage, "Scan Complete");
        NotifyUpdate();
    }

    private void NotifyUpdate()
    {
        OnProgressUpdated?.Invoke(new ScanProgressUpdate(
            ProgressPercentage,
            CurrentPhase,
            StatusMessage,
            IsScanning
        ));
    }
}
