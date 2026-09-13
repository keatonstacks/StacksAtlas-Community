using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using StacksAtlas.Core.Models;
using System.Collections.Concurrent;

namespace StacksAtlas.Core.Services.Devices;

/// <summary>
/// A high-performance regex matching engine using Rapid7 Recog XML signatures.
/// </summary>
public class RecogMatchingService
{
    private readonly ILogger<RecogMatchingService> _logger;
    private readonly List<RecogComponent> _httpTitleMatchers = [];
    private readonly List<RecogComponent> _snmpSysDescrMatchers = [];

    // Bounded Caches
    private readonly ConcurrentDictionary<string, (string? Vendor, string? Type, string? Model)> _httpCache = new();
    private readonly ConcurrentDictionary<string, (string? Vendor, string? Type, string? Model)> _snmpCache = new();
    private const int MaxCacheSize = 1000;

    public RecogMatchingService(ILogger<RecogMatchingService> logger)
    {
        _logger = logger;
        LoadMatchers();
    }

    private void LoadMatchers()
    {
        try
        {
            var assembly = typeof(RecogMatchingService).Assembly;

            // Load HTTP Titles
            using (var httpStream = assembly.GetManifestResourceStream("StacksAtlas.Core.Resources.Recog.http_servers.xml"))
            {
                if (httpStream != null)
                {
                    ParseXml(httpStream, _httpTitleMatchers);
                    _logger.LogDebug("Loaded {Count} Recog HTTP Title matchers.", _httpTitleMatchers.Count);
                }
                else
                {
                    _logger.LogWarning("Failed to find embedded resource: http_servers.xml");
                }
            }

            // Load SNMP SysDescr
            using (var snmpStream = assembly.GetManifestResourceStream("StacksAtlas.Core.Resources.Recog.snmp_sysDescr.xml"))
            {
                if (snmpStream != null)
                {
                    ParseXml(snmpStream, _snmpSysDescrMatchers);
                    _logger.LogDebug("Loaded {Count} Recog SNMP SysDescr matchers.", _snmpSysDescrMatchers.Count);
                }
                else
                {
                    _logger.LogWarning("Failed to find embedded resource: snmp_sysDescr.xml");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Recog matchers.");
        }
    }

    private void ParseXml(Stream stream, List<RecogComponent> targetList)
    {
        var doc = XDocument.Load(stream);
        // Recog V2 uses <fingerprint>, V1 used <match>
        var entries = doc.Descendants("fingerprint").Concat(doc.Descendants("match"));

        foreach (var entry in entries)
        {
            try
            {
                var patternStr = entry.Attribute("pattern")?.Value;
                if (string.IsNullOrWhiteSpace(patternStr)) continue;

                // Pre-compile the regex for max runtime performance
                var regex = new Regex(patternStr, RegexOptions.Compiled | RegexOptions.IgnoreCase);

                var component = new RecogComponent
                {
                    Pattern = regex,
                    ParamMaps = new Dictionary<string, string>()
                };

                // 1. Handle V1 attributes (match hw.vendor="...")
                component.Vendor = entry.Attribute("hw.vendor")?.Value ?? entry.Attribute("os.vendor")?.Value ?? entry.Attribute("service.vendor")?.Value;
                component.Type = entry.Attribute("hw.family")?.Value ?? entry.Attribute("os.family")?.Value;
                component.Model = entry.Attribute("hw.product")?.Value ?? entry.Attribute("os.product")?.Value ?? entry.Attribute("service.product")?.Value;

                // 2. Handle V2 params (fingerprint -> param name="..." value="..." pos="...")
                var paramsNodes = entry.Elements("param");
                foreach (var p in paramsNodes)
                {
                    var name = p.Attribute("name")?.Value;
                    var value = p.Attribute("value")?.Value;
                    var pos = p.Attribute("pos")?.Value;

                    if (string.IsNullOrEmpty(name)) continue;

                    // If it has a static value, assign it to the component directly if it matches our core fields
                    if (!string.IsNullOrEmpty(value))
                    {
                        if (name == "hw.vendor" || name == "os.vendor" || name == "service.vendor") component.Vendor ??= value;
                        else if (name == "hw.family" || name == "os.family") component.Type ??= value;
                        else if (name == "hw.product" || name == "os.product" || name == "service.product") component.Model ??= value;
                    }
                    
                    // If it has a position (group capture), map it for runtime interpolation
                    if (!string.IsNullOrEmpty(pos) && int.TryParse(pos, out int position))
                    {
                        // The engine expects "paramX" where X is the group index
                        component.ParamMaps[$"param{position}"] = name;
                    }
                }

                // 3. Fallback for legacy param attributes (param1="hw.product")
                var attributes = entry.Attributes();
                foreach (var attr in attributes)
                {
                    if (attr.Name.LocalName.StartsWith("param"))
                    {
                        component.ParamMaps[attr.Name.LocalName] = attr.Value;
                    }
                }

                // We only care if it can identify *something* useful
                if (!string.IsNullOrEmpty(component.Vendor) || !string.IsNullOrEmpty(component.Model) || component.ParamMaps.Count > 0)
                {
                    targetList.Add(component);
                }
            }
            catch (Exception ex)
            {
                // Regex parsing might fail on unsupported syntax, log edge cases trace-level
                _logger.LogTrace(ex, "Failed to compile Recog regex pattern.");
            }
        }
    }

    public List<ClassificationHint> GetHints(string? httpTitle, string? snmpSysDescr)
    {
        var hints = new List<ClassificationHint>();

        // 1. Try SNMP First (More reliable for definitive hardware)
        if (!string.IsNullOrWhiteSpace(snmpSysDescr))
        {
            var match = _snmpCache.TryGetValue(snmpSysDescr, out var cachedSnmp) 
                ? cachedSnmp 
                : EvaluateMatchers(snmpSysDescr, _snmpSysDescrMatchers);
            
            if (!_snmpCache.ContainsKey(snmpSysDescr)) CacheResult(_snmpCache, snmpSysDescr, match);

            if (match.Vendor != null || match.Model != null)
            {
                hints.Add(new ClassificationHint(
                    MapToDeviceType(match.Type),
                    match.Vendor,
                    match.Model,
                    90, // SNMP is high confidence
                    "Recog:SNMP"));
            }
        }

        // 2. Try HTTP Title 
        if (!string.IsNullOrWhiteSpace(httpTitle))
        {
            var match = _httpCache.TryGetValue(httpTitle, out var cachedHttp) 
                ? cachedHttp 
                : EvaluateMatchers(httpTitle, _httpTitleMatchers);
            
            if (!_httpCache.ContainsKey(httpTitle)) CacheResult(_httpCache, httpTitle, match);

            if (match.Vendor != null || match.Model != null)
            {
                hints.Add(new ClassificationHint(
                    MapToDeviceType(match.Type),
                    match.Vendor,
                    match.Model,
                    70, // HTTP is medium confidence
                    "Recog:HTTP"));
            }
        }

        return hints;
    }

    private DeviceType MapToDeviceType(string? recogType)
    {
        if (string.IsNullOrEmpty(recogType)) return DeviceType.Unknown;
        var lower = recogType.ToLowerInvariant();
        if (lower.Contains("switch")) return DeviceType.Network_Switch;
        if (lower.Contains("router")) return DeviceType.Network_Router;
        if (lower.Contains("ap") || lower.Contains("access point")) return DeviceType.Network_AP;
        if (lower.Contains("camera")) return DeviceType.Video_Camera;
        if (lower.Contains("printer")) return DeviceType.Printer;
        if (lower.Contains("storage") || lower.Contains("nas")) return DeviceType.Storage_NAS;
        if (lower.Contains("phone") || lower.Contains("mobile") || lower.Contains("ios") || lower.Contains("android")) return DeviceType.Mobile;
        if (lower.Contains("server") || lower.Contains("vmware") || lower.Contains("hyper-v")) return DeviceType.Server;
        if (lower.Contains("display") || lower.Contains("tv") || lower.Contains("monitor") || lower.Contains("projector")) return DeviceType.Video_Display;
        if (lower.Contains("audio") || lower.Contains("speaker") || lower.Contains("amplifier") || lower.Contains("dsp")) return DeviceType.Audio_DSP;
        if (lower.Contains("control") || lower.Contains("processor") || lower.Contains("crestron") || lower.Contains("q-sys")) return DeviceType.Control_Processor;
        return DeviceType.Unknown;
    }

    private void CacheResult(ConcurrentDictionary<string, (string?, string?, string?)> cache, string key, (string?, string?, string?) result)
    {
        if (cache.Count >= MaxCacheSize)
        {
            cache.Clear(); // Soft clear to prevent unbounded memory growth
            _logger.LogDebug("Recog cache cleared after reaching MaxCacheSize of {Size}", MaxCacheSize);
        }
        cache.TryAdd(key, result);
    }

    private (string? Vendor, string? Type, string? Model) EvaluateMatchers(string input, List<RecogComponent> matchers)
    {
        foreach (var component in matchers)
        {
            var match = component.Pattern.Match(input);
            if (match.Success)
            {
                string? finalVendor = component.Vendor;
                string? finalType = component.Type;
                string? finalModel = component.Model;

                // Process parameter interpolations (e.g. param1="os.product" -> group(1))
                if (component.ParamMaps.Count > 0)
                {
                    for (int i = 1; i < match.Groups.Count; i++)
                    {
                        string paramKey = $"param{i}";
                        if (component.ParamMaps.TryGetValue(paramKey, out string? mappedProperty) && match.Groups[i].Success)
                        {
                            string capturedValue = match.Groups[i].Value.Trim();
                            
                            if (mappedProperty == "os.product" || mappedProperty == "hw.product" || mappedProperty == "service.product")
                            {
                                // If the XML specifies a static model AND a captured param, we append the capture (e.g. "Catalyst " + "2960")
                                finalModel = string.IsNullOrEmpty(finalModel) ? capturedValue : $"{finalModel} {capturedValue}";
                            }
                            else if (mappedProperty == "os.vendor" || mappedProperty == "hw.vendor")
                            {
                                finalVendor = capturedValue;
                            }
                            else if (mappedProperty == "os.family" || mappedProperty == "hw.family")
                            {
                                finalType = capturedValue;
                            }
                        }
                    }
                }

                // Cleanup generic mappings
                if (string.Equals(finalType, "linux", StringComparison.OrdinalIgnoreCase)) finalType = "Linux Device";
                if (string.Equals(finalType, "windows", StringComparison.OrdinalIgnoreCase)) finalType = "Windows Device";

                return (finalVendor, finalType, finalModel);
            }
        }

        return (null, null, null);
    }

    private class RecogComponent
    {
        public required Regex Pattern { get; set; }
        public string? Vendor { get; set; }
        public string? Type { get; set; }
        public string? Model { get; set; }
        public required Dictionary<string, string> ParamMaps { get; set; }
    }
}
