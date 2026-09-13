using System.Diagnostics;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using StacksAtlas.Core.Helpers;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Services.Devices;

namespace StacksAtlas.Core.Services.Scanning;

public class NmapScanResult
{
    public List<ServiceDetail> Services { get; set; } = new();
    public string? DetectedOS { get; set; }
    public int OSAccuracy { get; set; }
}

public interface INmapService
{
    Task<NmapScanResult> ScanDeviceAsync(Device device, IProgress<int>? progress = null, CancellationToken token = default);
    Task<List<HostScanResult>> ScanSubnetAsync(string cidr, CancellationToken token = default);
    bool IsNmapAvailable();
    bool HasPcapDriver();
    bool HasNmapBinary();
}

public class NmapService : INmapService
{
    private readonly ILogger<NmapService> _logger;
    private string _resolvedNmapPath = "nmap";
    private bool _hasLoggedDetection;
    private static readonly string _discoveryPorts = string.Join(",", PortScanner.ScannedPorts);

    private readonly StacksAtlas.Core.Abstractions.IClock _clock;

    public NmapService(ILogger<NmapService> logger, StacksAtlas.Core.Abstractions.IClock clock)
    {
        _logger = logger;
        _clock = clock;
    }

    public bool HasPcapDriver()
    {
        if (global::System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(global::System.Runtime.InteropServices.OSPlatform.Linux) ||
            global::System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(global::System.Runtime.InteropServices.OSPlatform.OSX)) 
            return true; // We rely on libpcap/native pcap from the Unix host

        try
        {
            // Standard location for wpcap.dll (WinPcap or Npcap in Compatibility Mode)
            var systemPath = Environment.GetFolderPath(Environment.SpecialFolder.System);
            var pcapPath = Path.Combine(systemPath, "wpcap.dll");
            if (File.Exists(pcapPath)) return true;

            // Npcap-specific location (if not in compatibility mode)
            var npcapPath = Path.Combine(systemPath, "Npcap", "wpcap.dll");
            if (File.Exists(npcapPath)) return true;

            // Check SysWOW64 as well on 64-bit systems if we are a 64-bit process
            if (Environment.Is64BitProcess)
            {
                var sysWow64Path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SysWOW64", "wpcap.dll");
                if (File.Exists(sysWow64Path)) return true;
                
                var npcapWow64Path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SysWOW64", "Npcap", "wpcap.dll");
                if (File.Exists(npcapWow64Path)) return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking for Pcap driver.");
            return false;
        }
    }

    public bool HasNmapBinary()
    {
        if (global::System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(global::System.Runtime.InteropServices.OSPlatform.Linux))
        {
            if (CheckNmapPath("nmap"))
            {
                _resolvedNmapPath = "nmap";
                return true;
            }
            return false;
        }

        if (global::System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(global::System.Runtime.InteropServices.OSPlatform.OSX))
        {
            var macPaths = new[] 
            { 
                "/opt/homebrew/bin/nmap", 
                "/usr/local/bin/nmap", 
                "/usr/bin/nmap",
                "/opt/local/bin/nmap",
                "/sw/bin/nmap",
                "/Applications/nmap.app/Contents/MacOS/nmap",
                "/Applications/nmap.app/Contents/Resources/bin/nmap",
                "/Applications/Nmap.app/Contents/MacOS/nmap",
                "/Applications/Nmap.app/Contents/Resources/bin/nmap",
                "/Applications/Zenmap.app/Contents/Resources/bin/nmap"
            };

            foreach (var path in macPaths)
            {
                if (File.Exists(path))
                {
                    _resolvedNmapPath = path;
                    if (!_hasLoggedDetection)
                    {
                        _logger.LogInformation("Nmap detected at {Path}", _resolvedNmapPath);
                        _hasLoggedDetection = true;
                    }
                    return true;
                }
            }
            return false;
        }

        // 1. Check if "nmap" is in PATH
        if (CheckNmapPath("nmap"))
        {
            _resolvedNmapPath = "nmap";
            return true;
        }

        // 2. Check common Windows paths
        var commonPaths = new[] 
        {
            @"C:\Program Files (x86)\Nmap\nmap.exe",
            @"C:\Program Files\Nmap\nmap.exe"
        };

        foreach (var path in commonPaths)
        {
            if (File.Exists(path))
            {
                _resolvedNmapPath = path;
                return true;
            }
        }

        return false;
    }

    public bool IsNmapAvailable()
    {
        return HasPcapDriver() && HasNmapBinary();
    }


    private bool CheckNmapPath(string pathCommand)
    {
        try
        {
            using var process = new Process();
            process.StartInfo.FileName = pathCommand;
            process.StartInfo.Arguments = "--version";
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.WorkingDirectory = Path.GetTempPath(); // FIX: Explicitly set working directory
            process.StartInfo.CreateNoWindow = true;
            process.Start();
            
            // Wait with a small timeout for path checks
            bool exited = process.WaitForExit(2000);
            if (!exited) 
            {
                process.Kill();
                return false;
            }

            bool success = process.ExitCode == 0;
            if (!success)
            {
                _logger.LogInformation("Diagnostic: Nmap check returned non-zero exit code {Code} for command '{Cmd}'", process.ExitCode, pathCommand);
            }
            return success;
        }
        catch (Exception ex)
        {
            // Only log as information if it's truly a missing file (not in PATH)
            if (ex.Message.Contains("No such file or directory") || ex.Message.Contains("The system cannot find the file specified"))
            {
                _logger.LogInformation("Diagnostic: Command '{Cmd}' not found in system PATH. Checking hardcoded alternates...", pathCommand);
            }
            else
            {
                _logger.LogInformation("Diagnostic: Nmap check failed for '{Cmd}'. Message: {Msg}", pathCommand, ex.Message);
            }
            return false;
        }
    }

    public async Task<NmapScanResult> ScanDeviceAsync(Device device, IProgress<int>? progress = null, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(device.IpAddress))
        {
            _logger.LogWarning("Skipping Deep Scan for device {DeviceId}: No IP Address", device.Id);
            return new NmapScanResult();
        }

        if (!NetworkHelper.IsSafeNmapScanTarget(device.IpAddress))
        {
            _logger.LogWarning("Skipping Deep Scan for device {DeviceId}: Invalid or unsafe scan target {Ip}", device.Id, device.IpAddress);
            throw new InvalidOperationException("Device IP address is not a valid scan target.");
        }

        var scanTarget = NetworkHelper.NormalizeIpAddress(device.IpAddress);

        // Create a temp file for XML output
        var tempXmlFile = Path.GetTempFileName();

        try 
        {
            // -O: OS detection
            // -Pn: Treat all hosts as online -- skip host discovery
            // -sV: Version detection
            // -T4: Aggressive timing
            // --open: Only open ports
            // --stats-every 2s: Output progress line every 2 seconds
            // -oX <file>: Output XML to file
            // --script http-default-accounts: Check for default web credentials
            // --privileged: Required for OS detection on macOS
            string arguments = $"-O -Pn -sV -T4 --open --script http-default-accounts --privileged --stats-every 2s -oX \"{tempXmlFile}\" {scanTarget}";

            var processStartInfo = new ProcessStartInfo
            {
                FileName = _resolvedNmapPath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetTempPath(), // Avoid bundle permission issues
                StandardOutputEncoding = Encoding.UTF8
            };

            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            using var process = new Process { StartInfo = processStartInfo };

            process.OutputDataReceived += (sender, e) => 
            { 
                if (e.Data != null) 
                {
                    outputBuilder.AppendLine(e.Data);
                    ParseProgress(e.Data, progress);
                }
            };
            process.ErrorDataReceived += (sender, e) => 
            { 
                if (e.Data != null) 
                {
                    errorBuilder.AppendLine(e.Data);
                    ParseProgress(e.Data, progress);
                }
            };

            _logger.LogInformation("Starting Nmap scan for {IpAddress} using {NmapPath}...", scanTarget, _resolvedNmapPath);

            using var progressCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            var scanStartUtc = _clock.UtcNow;
            var heartbeatTask = progress == null
                ? Task.CompletedTask
                : Task.Run(async () =>
                {
                    var lastReported = 0;
                    try
                    {
                        while (!progressCts.Token.IsCancellationRequested)
                        {
                            await Task.Delay(2000, progressCts.Token);
                            var elapsed = (_clock.UtcNow - scanStartUtc).TotalSeconds;
                            var synthetic = Math.Min(90, (int)(elapsed / 120.0 * 100));
                            if (synthetic > lastReported)
                            {
                                lastReported = synthetic;
                                progress.Report(synthetic);
                            }
                        }
                    }
                    catch (OperationCanceledException) { }
                }, progressCts.Token);

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(token);
            progressCts.Cancel();
            try { await heartbeatTask; } catch { /* heartbeat cancelled */ }

            if (process.ExitCode != 0)
            {
                _logger.LogError("Nmap scan failed for {IpAddress}. Exit Code: {ExitCode}. Error: {Error}", device.IpAddress, process.ExitCode, errorBuilder);
                throw new Exception($"Nmap failed with exit code {process.ExitCode}");
            }
            
            // Read the XML from the temp file
            if (File.Exists(tempXmlFile))
            {
                string xmlOutput = await File.ReadAllTextAsync(tempXmlFile, token);
                return ParseNmapXml(xmlOutput, device.Id);
            }
            return new NmapScanResult();
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Nmap scan cancelled for {IpAddress}", device.IpAddress);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing Nmap scan for {IpAddress}", device.IpAddress);
            throw;
        }
        finally
        {
            if (File.Exists(tempXmlFile))
            {
                try { File.Delete(tempXmlFile); } catch { }
            }
        }
    }

    public async Task<List<HostScanResult>> ScanSubnetAsync(string cidr, CancellationToken token = default)
    {
        if (!IsNmapAvailable()) return new List<HostScanResult>();

        // -p: Scan specific ports (SSH, HTTP, etc.)
        // --open: Only show open ports
        // -n: Never do DNS resolution (fast)
        // -oX -: Output XML to stdout
        // --max-retries 2: Balance speed and reliability
        // --privileged: Force raw socket access (ARP/ICMP)
        string arguments = $"-p {_discoveryPorts} --open -n --max-retries 2 --privileged -oX - {cidr}";
        _logger.LogInformation("Nmap Discovery starting for {Cidr}", cidr);

        var processStartInfo = new ProcessStartInfo
        {
            FileName = _resolvedNmapPath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetTempPath(), // Avoid app folder permission issues
            StandardOutputEncoding = Encoding.UTF8
        };

        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();
        using var process = new Process { StartInfo = processStartInfo };

        process.OutputDataReceived += (sender, e) => { if (e.Data != null) outputBuilder.AppendLine(e.Data); };
        process.ErrorDataReceived += (sender, e) => { if (e.Data != null) errorBuilder.AppendLine(e.Data); };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync(token);

            if (errorBuilder.Length > 0)
            {
                var error = errorBuilder.ToString();
                _logger.LogWarning("Nmap Discovery Stderr: {Error}", error);
                
                // GHOST-PING PROTECTION: Detect if the system is dropping packets/sockets
                if (error.Contains("Resource temporarily unavailable") || error.Contains("sendto") || error.Contains("EAGAIN"))
                {
                    _logger.LogError("CRITICAL: Nmap discovery is unreliable due to network stack pressure. Aborting sweep to prevent false positives.");
                    return new List<HostScanResult>();
                }
            }

            if (process.ExitCode != 0)
            {
                _logger.LogError("Nmap Discovery failed with exit code {Code}", process.ExitCode);
                return new List<HostScanResult>();
            }

            var xml = outputBuilder.ToString();
            
            if (string.IsNullOrWhiteSpace(xml))
            {
                _logger.LogWarning("Nmap Discovery returned empty stdout.");
                return new List<HostScanResult>();
            }

            return ParseSubnetXml(xml);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing Nmap subnet discovery");
            return new List<HostScanResult>();
        }
    }

    private List<HostScanResult> ParseSubnetXml(string xml)
    {
        var results = new List<HostScanResult>();
        try
        {
            var doc = XDocument.Parse(xml);
            var hosts = doc.Descendants("host");

            foreach (var hostElem in hosts)
            {
                var status = hostElem.Element("status")?.Attribute("state")?.Value;
                if (status != "up") continue;

                var ipAddr = hostElem.Elements("address")
                    .FirstOrDefault(a => a.Attribute("addrtype")?.Value == "ipv4")?
                    .Attribute("addr")?.Value;

                if (string.IsNullOrEmpty(ipAddr)) continue;

                var macAddr = hostElem.Elements("address")
                    .FirstOrDefault(a => a.Attribute("addrtype")?.Value == "mac")?
                    .Attribute("addr")?.Value;
                
                var vendor = hostElem.Elements("address")
                    .FirstOrDefault(a => a.Attribute("addrtype")?.Value == "mac")?
                    .Attribute("vendor")?.Value ?? "Unknown Vendor";

                var macNormalized = macAddr != null && DeviceMacNormalizer.TryParse(macAddr, out var parsedMac)
                    ? parsedMac
                    : string.Empty;

                var openPorts = hostElem.Descendants("port")
                    .Where(p => p.Element("state")?.Attribute("state")?.Value == "open")
                    .Select(p => int.Parse(p.Attribute("portid")?.Value ?? "0"))
                    .Where(p => p > 0)
                    .ToList();

                results.Add(new HostScanResult(
                    Ip: ipAddr, 
                    IsOnline: true, 
                    RoundtripTimeMs: 0, 
                    Mac: macNormalized, 
                    Vendor: vendor, 
                    Hostname: null, 
                    Name: null, 
                    Type: "Unknown", 
                    Model: null, 
                    ConfidenceScore: 0, 
                    OpenPorts: openPorts, 
                    Error: null));
            }
        }
        catch { }
        return results;
    }

    private void ParseProgress(string line, IProgress<int>? progress)
    {
        if (progress == null) return;
        
        // Nmap stats lines: "About 50.00% done" or "About 50.05% done"
        if (line.Contains('%'))
        {
            try 
            {
                var idx = line.IndexOf("About ", StringComparison.Ordinal);
                if (idx >= 0)
                {
                    var afterAbout = line[(idx + 6)..];
                    var percentPart = afterAbout.Split('%')[0].Trim();
                    if (double.TryParse(percentPart, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double p))
                    {
                        progress.Report((int)Math.Clamp(p, 0, 99));
                    }
                }
            }
            catch { }
        }
    }

    private NmapScanResult ParseNmapXml(string xmlData, Guid deviceId)
    {
        var result = new NmapScanResult();
        if (string.IsNullOrWhiteSpace(xmlData)) return result;

        try
        {
            // Need to handle XML namespaces if Nmap uses them, but usually standard XDocument works fine.
            // Nmap XML can be complex.
            var doc = XDocument.Parse(xmlData);
            
            // Navigate to <host> -> <ports> -> <port>
            var ports = doc.Descendants("port");

            foreach (var portElem in ports)
            {
                var serviceDetail = new ServiceDetail
                {
                    DeviceId = deviceId,
                    ScannedAtUtc = _clock.UtcNow
                };

                // Port and Protocol
                if (int.TryParse(portElem.Attribute("portid")?.Value, out int portId))
                {
                    serviceDetail.Port = portId;
                }
                serviceDetail.Protocol = portElem.Attribute("protocol")?.Value ?? "tcp";

                // State
                var stateElem = portElem.Element("state");
                serviceDetail.State = stateElem?.Attribute("state")?.Value ?? "unknown";

                // Service
                var serviceElem = portElem.Element("service");
                if (serviceElem != null)
                {
                    serviceDetail.ServiceName = serviceElem.Attribute("name")?.Value;
                    serviceDetail.Product = serviceElem.Attribute("product")?.Value;
                    serviceDetail.Version = serviceElem.Attribute("version")?.Value;
                    serviceDetail.ExtraInfo = serviceElem.Attribute("extrainfo")?.Value;
                }

                // Scripts (e.g. http-default-accounts)
                var scripts = portElem.Elements("script");
                foreach (var script in scripts)
                {
                    var id = script.Attribute("id")?.Value;
                    var output = script.Attribute("output")?.Value;
                    if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(output))
                    {
                        // Prefix script output to distinguish it in ExtraInfo
                        var finding = $"[{id}]: {output.Trim().Replace("\n", " ")}";
                        serviceDetail.ExtraInfo = string.IsNullOrEmpty(serviceDetail.ExtraInfo) 
                            ? finding 
                            : $"{serviceDetail.ExtraInfo} | {finding}";
                    }
                }

                result.Services.Add(serviceDetail);
            }

            // --- OS DETECTION ---
            var osElem = doc.Descendants("osmatch").OrderByDescending(o => int.Parse(o.Attribute("accuracy")?.Value ?? "0")).FirstOrDefault();
            if (osElem != null)
            {
                result.DetectedOS = osElem.Attribute("name")?.Value;
                result.OSAccuracy = int.Parse(osElem.Attribute("accuracy")?.Value ?? "0");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing Nmap XML output");
        }

        return result;
    }
}
