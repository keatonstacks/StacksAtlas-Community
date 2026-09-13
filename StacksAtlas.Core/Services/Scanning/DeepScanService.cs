using System.Collections.Concurrent;
using System.Threading.Channels;
using LiteDB;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StacksAtlas.Core.Models;

namespace StacksAtlas.Core.Services.Scanning;

public record DeepScanRequest(Guid DeviceId);

public interface IDeepScanService
{
    void QueueScan(Guid deviceId);
    (string Status, int Progress) GetScanStatus(Guid deviceId);
    bool IsNmapAvailable();
}

public class DeepScanService : BackgroundService, IDeepScanService
{
    private readonly ILogger<DeepScanService> _logger;
    private readonly INmapService _nmapService;
    private readonly SecurityAuditService _securityAuditer;
    private readonly ILiteDatabase _db;
    private readonly Channel<DeepScanRequest> _scanQueue;
    
    // Track active scans for UI status updates: Status, Progress%
    // Tuple: (Status string, Progress int)
    private readonly ConcurrentDictionary<Guid, (string Status, int Progress)> _activeScans = new();

    private readonly StacksAtlas.Core.Abstractions.IClock _clock;

    public DeepScanService(ILogger<DeepScanService> logger, INmapService nmapService, SecurityAuditService securityAuditer, ILiteDatabase db, StacksAtlas.Core.Abstractions.IClock clock)
    {
        _logger = logger;
        _nmapService = nmapService;
        _securityAuditer = securityAuditer;
        _db = db;
        _clock = clock;
        _scanQueue = Channel.CreateUnbounded<DeepScanRequest>();
    }

    public bool IsNmapAvailable() => _nmapService.IsNmapAvailable();

    public void QueueScan(Guid deviceId)
    {
        _scanQueue.Writer.TryWrite(new DeepScanRequest(deviceId));
    }

    public (string Status, int Progress) GetScanStatus(Guid deviceId)
    {
        if (_activeScans.TryGetValue(deviceId, out var state))
        {
            return state;
        }
        return ("idle", 0);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Deep Scan Service is starting.");

        await foreach (var request in _scanQueue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessScanAsync(request, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing deep scan for device {DeviceId}", request.DeviceId);
                _activeScans.TryRemove(request.DeviceId, out _);
            }
        }
    }

    private async Task ProcessScanAsync(DeepScanRequest request, CancellationToken token)
    {
        _activeScans[request.DeviceId] = ("running", 0);
        
        try
        {
            var devices = _db.GetCollection<Device>("devices");
            var device = devices.FindById(request.DeviceId);

            if (device == null)
            {
                _logger.LogWarning("Device {DeviceId} not found for deep scan.", request.DeviceId);
                return;
            }

            if (!_nmapService.IsNmapAvailable())
            {
                _logger.LogError("Nmap is not available. Cannot perform deep scan.");
                _activeScans[request.DeviceId] = ("error: nmap missing", 0);
                return; 
            }

            _logger.LogInformation("Starting deep scan for device {DeviceName} ({IpAddress})", device.Name, device.IpAddress);

            // Create progress handler
            var progressHandler = new Progress<int>(percent => 
            {
                // Update progress, keep status "running"
                _activeScans[request.DeviceId] = ("running", percent);
            });

            var scanResult = await _nmapService.ScanDeviceAsync(device, progressHandler, token);
            var results = scanResult.Services;

            // Update Database
            var services = _db.GetCollection<ServiceDetail>("service_details");
            
            // Remove existing services for this device
            services.DeleteMany(x => x.DeviceId == request.DeviceId);

            // Intelligence Extraction
            var intelligenceFindings = new List<string>();
            foreach(var result in results)
            {
                if (!string.IsNullOrEmpty(result.ExtraInfo) && result.ExtraInfo.Contains("[http-default-accounts]"))
                {
                    // Extract the finding
                    var finding = result.ExtraInfo.Split("[http-default-accounts]:").Last().Trim();
                    intelligenceFindings.Add($"Default credentials found on port {result.Port}: {finding}");
                }
            }

            // Insert new results
            if (results.Any())
            {
                services.Insert(results);

                // Sync found ports to Device model
                device.OpenPorts = results.Select(x => x.Port).Distinct().OrderBy(p => p).ToList();
            }
            else 
            {
                device.OpenPorts = new List<int>();
            }

            // Update Intelligence Findings
            device.DeepScanIssues = intelligenceFindings;

            // Update OS Information
            if (!string.IsNullOrEmpty(scanResult.DetectedOS))
            {
                device.OperatingSystem = scanResult.DetectedOS;
                _logger.LogInformation("OS Detected (Nmap -O) for {DeviceName}: {OS}", device.Name, device.OperatingSystem);
            }
            else
            {
                // Fallback: Heuristic OS Detection from Service Banners
                var inferredOS = InferOSFromServices(results);
                if (!string.IsNullOrEmpty(inferredOS))
                {
                    device.OperatingSystem = inferredOS;
                    _logger.LogInformation("OS Inferred (Heuristic) for {DeviceName}: {OS}", device.Name, device.OperatingSystem);
                }
            }

            // Trigger immediate Audit Refresh
            var audit = _securityAuditer.AuditDevice(device);
            device.SecurityGrade = audit.Grade;
            device.SecurityIssues = audit.Issues;

            // Update Device timestamp/status/ports
            device.Status = "online";
            device.LastSeen = _clock.UtcNow;
            devices.Update(device);

            _logger.LogInformation("Deep scan completed for {DeviceName}. Found {Count} services and {IntelCount} intel findings. OS: {OS}", 
                device.Name, results.Count, intelligenceFindings.Count, device.OperatingSystem ?? "Unknown");
        }
        finally
        {
            _activeScans.TryRemove(request.DeviceId, out _);
        }
    }


    private string? InferOSFromServices(List<ServiceDetail> services)
    {
        // 1. Strong Indicators (Explicit Product Names)
        if (services.Any(s => (s.Product ?? "").Contains("Windows", StringComparison.OrdinalIgnoreCase)))
        {
            // Try to refine version if possible
            if (services.Any(s => (s.Product ?? "").Contains("Windows 7", StringComparison.OrdinalIgnoreCase))) return "Microsoft Windows 7";
            if (services.Any(s => (s.Product ?? "").Contains("Windows XP", StringComparison.OrdinalIgnoreCase))) return "Microsoft Windows XP";
            if (services.Any(s => (s.Product ?? "").Contains("2008", StringComparison.OrdinalIgnoreCase))) return "Microsoft Windows Server 2008";
            
            return "Microsoft Windows (Detected)";
        }

        if (services.Any(s => (s.Product ?? "").Contains("Synology DSM", StringComparison.OrdinalIgnoreCase)))
            return "Synology DiskStation Manager (Linux)";

        if (services.Any(s => (s.Product ?? "").Contains("Ubuntu", StringComparison.OrdinalIgnoreCase)))
            return "Linux (Ubuntu)";
        
        if (services.Any(s => (s.Product ?? "").Contains("Debian", StringComparison.OrdinalIgnoreCase)))
            return "Linux (Debian)";

        // 2. Port-Based Heuristics ( weaker but useful)
        // Port 135 (RPC) + 445 (SMB) usually implies Windows
        if (services.Any(s => s.Port == 135)) return "Microsoft Windows";

        // 3. Service Name Heuristics
        if (services.Any(s => (s.ServiceName ?? "").Contains("microsoft-ds", StringComparison.OrdinalIgnoreCase))) return "Microsoft Windows";

        return null;
    }
}
