using StacksAtlas.Core.Models;

namespace StacksAtlas.Core.Services.Scanning;

/// <summary>
/// A No-Op implementation of Nmap services for Hub mode.
/// </summary>
public class NoOpNmapService : INmapService
{
    public bool HasNmapBinary() => false;
    public bool HasPcapDriver() => false;
    public bool IsNmapAvailable() => false;

    public Task<NmapScanResult> ScanDeviceAsync(Device device, IProgress<int>? progress = null, CancellationToken token = default) 
        => Task.FromResult(new NmapScanResult());

    public Task<List<HostScanResult>> ScanSubnetAsync(string cidr, CancellationToken token = default) 
        => Task.FromResult(new List<HostScanResult>());
}
