using System.Reflection;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Services.Scanning;
using StacksAtlas.Core.Services.Network;

namespace StacksAtlas.Core.State;

public class SystemStateProvider(
    INetworkInterfaceService networkService, 
    INmapService nmapService,
    ScanProgressService progressService)
{
    private readonly INetworkInterfaceService _networkService = networkService;
    private readonly INmapService _nmapService = nmapService;
    private readonly ScanProgressService _progressService = progressService;
    
    private bool? _hasPcap;
    private bool? _hasNmap;
    private string? _cachedVersion;

    public bool IsScanning { get; set; }
    public bool IsDatabaseReady { get; set; } = false;
    public DateTime LastSweepTime { get; set; }

    public StacksAtlas.Core.Models.SystemStatus GetCurrentStatus()
    {
        var activeInterface = _networkService.GetActiveInterface();
        var missing = new List<string>();
        
        // Cache-aware dependency checks to avoid redundant I/O
        _hasPcap ??= _nmapService.HasPcapDriver();
        _hasNmap ??= _nmapService.HasNmapBinary();

        if (!_hasPcap.Value) missing.Add("Npcap Driver");
        if (!_hasNmap.Value) missing.Add("Nmap Binary");
        
        _cachedVersion ??= Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "1.2.5";

        return new()
        {
            IsScanning = IsScanning || _progressService.IsScanning,
            EnginePhase = _progressService.IsScanning ? _progressService.CurrentPhase : (IsScanning ? "Ping Probe" : "Idle"),
            DatabaseReady = IsDatabaseReady,
            LastSweepTime = LastSweepTime,
            Status = IsDatabaseReady ? "Online" : "Initializing",
            HostIp = activeInterface.IpAddress,
            ActiveInterface = $"{activeInterface.Name} ({activeInterface.Description})",
            ScanningRange = _networkService.GetScannerCidr(),
            Version = _cachedVersion.Split('+')[0], // Clean version (no commit hash)
            MissingDependencies = missing
        };
    }
}
