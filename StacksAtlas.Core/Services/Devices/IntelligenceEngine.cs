using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using StacksAtlas.Core.Models;

namespace StacksAtlas.Core.Services.Devices;

/// <summary>
/// The central intelligence engine for StacksAtlas. 
/// Synthesizes multiple evidence sources into a single weighted device classification.
/// </summary>
public class IntelligenceEngine
{
    private readonly ILogger<IntelligenceEngine> _logger;
    private readonly FingerprintLibrary _library;
    private readonly List<(Regex Pattern, ClassificationRule Rule)> _compiledRules;
    public string LocalMachineMac { get; set; } = string.Empty;

    public IntelligenceEngine(ILogger<IntelligenceEngine> logger)
    {
        _logger = logger;
        _library = LoadLibrary();
        _compiledRules = CompileRules(_library);
    }

    /// <summary>
    /// Processes all available data for a device and returns the best classification hints.
    /// </summary>
    public List<ClassificationHint> GetHints(
        string? name, 
        string? hostname, 
        string? vendor, 
        string? mac, 
        List<int>? ports,
        (string? Vendor, string? Type, string? Model)? recogMatch = null)
    {
        var hints = new List<ClassificationHint>();

        // 0. SELF-AWARENESS
        if (!string.IsNullOrEmpty(LocalMachineMac) && !string.IsNullOrEmpty(mac))
        {
            if (string.Equals(mac.Replace(":", "").Replace("-", "").ToUpperInvariant(), 
                             LocalMachineMac.Replace(":", "").Replace("-", "").ToUpperInvariant(), 
                             StringComparison.OrdinalIgnoreCase))
            {
                hints.Add(new ClassificationHint(DeviceType.Network_Infrastructure, "StacksAtlas", "StacksAtlas Host Node", 100, "SelfAwareness"));
                return hints;
            }
        }

        // 1. RULE-BASED MATCHING (Regex on Name/Hostname)
        string search = $"{(name ?? "")} {(hostname ?? "")}".ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(search))
        {
            foreach (var (pattern, rule) in _compiledRules)
            {
                if (pattern.IsMatch(search))
                {
                    hints.Add(new ClassificationHint(
                        rule.Type, 
                        rule.Vendor, 
                        rule.Model, 
                        rule.Confidence, 
                        "PatternLibrary"));
                    break; // Pick the first/best regex match
                }
            }
        }

        // 2. PORT FINGERPRINTING
        if (ports != null && ports.Any())
        {
            string? ouiVendorName = null;
            DeviceType ouiTypeHint = DeviceType.Unknown;
            if (!string.IsNullOrEmpty(mac))
            {
                var (ouiV, ouiT) = OuiDatabase.Lookup(NormalizeMacForOui(mac));
                if (ouiV != "Unknown Vendor")
                {
                    ouiVendorName = ouiV;
                    ouiTypeHint = ouiT;
                }
            }
            else if (!string.IsNullOrEmpty(vendor) && vendor != "Unknown Vendor" && vendor != "Randomized MAC Address")
            {
                ouiVendorName = vendor;
            }

            foreach (var rule in _library.PortRules)
            {
                if (!ports.Contains(rule.Port))
                    continue;

                if (ouiVendorName != null && PortRuleConflictsWithOui(rule.Vendor, rule.Type, ouiVendorName, ouiTypeHint))
                    continue;

                hints.Add(new ClassificationHint(
                    rule.Type,
                    rule.Vendor,
                    rule.Model,
                    rule.Confidence,
                    $"Port:{rule.Port}"));
            }
        }

        // 3. RECOG INTEGRATION
        if (recogMatch.HasValue)
        {
            var (rVendor, rType, rModel) = recogMatch.Value;
            if (rVendor != null || rModel != null)
            {
                hints.Add(new ClassificationHint(
                    MapRecogType(rType), 
                    rVendor, 
                    rModel, 
                    75, // Recog is high confidence
                    "Recog"));
            }
        }

        // 4. OUI FALLBACK
        if (!string.IsNullOrEmpty(vendor) && vendor != "Unknown Vendor")
        {
            hints.Add(new ClassificationHint(
                DeviceType.Unknown, 
                vendor, 
                null, 
                95, // OUI is high confidence for Vendor
                "OUI"));
        }

        return hints;
    }

    /// <summary>
    /// Synthesizes multiple hints into a single "Winner" classification.
    /// Vendor: Authoritative sources (OUI, SelfAwareness, Recog, PatternLibrary) always outrank port-derived guesses.
    /// Type: Weighted sum across all sources (ports are legitimately useful for type classification).
    /// </summary>
    public (DeviceType Type, string? Model, string? Vendor, int Confidence, IdentitySource Source) Synthesize(List<ClassificationHint> hints)
    {
        if (!hints.Any())
            return (DeviceType.Unknown, null, null, 0, IdentitySource.Unknown);

        // --- PASS 1: VENDOR (Authoritative sources are king) ---
        // Priority order: SelfAwareness > OUI > Recog > PatternLibrary > Port hints
        string[] authoritativeSources = ["SelfAwareness", "OUI", "Recog", "PatternLibrary"];

        var vendorHints = hints
            .Where(h => !string.IsNullOrEmpty(h.Vendor) && h.Vendor != "Unknown Vendor")
            .ToList();

        // First, check if any authoritative source provided a vendor
        var authoritativeVendor = vendorHints
            .Where(h => authoritativeSources.Contains(h.Source))
            .OrderByDescending(h => h.Confidence)
            .FirstOrDefault();

        string? bestVendor;
        int vendorConfidence;

        if (authoritativeVendor != null)
        {
            // MAC/OUI/Recog says who made this device  -  that's the final answer
            bestVendor = authoritativeVendor.Vendor;
            vendorConfidence = authoritativeVendor.Confidence;
        }
        else
        {
            // No authoritative source  -  fall back to the old sum-based approach for port hints
            var vendorWinner = vendorHints
                .GroupBy(h => h.Vendor)
                .Select(g => new { Vendor = g.Key, TotalConfidence = g.Sum(h => h.Confidence) })
                .OrderByDescending(x => x.TotalConfidence)
                .FirstOrDefault();

            bestVendor = vendorWinner?.Vendor;
            vendorConfidence = vendorWinner?.TotalConfidence ?? 0;
        }

        // --- PASS 2: TYPE/MODEL (All sources contribute, sum-based) ---
        var typeWinner = hints
            .Where(h => h.Type != DeviceType.Unknown)
            .GroupBy(h => new { h.Type, h.Model })
            .Select(g => new { g.Key.Type, g.Key.Model, Confidence = g.Sum(h => h.Confidence) })
            .OrderByDescending(x => x.Confidence)
            .FirstOrDefault();

        DeviceType bestType = typeWinner?.Type ?? DeviceType.Unknown;
        string? bestModel = typeWinner?.Model;
        int typeConfidence = typeWinner?.Confidence ?? 0;

        ApplyOuiConflictGuard(hints, ref bestType, ref bestModel, ref typeConfidence);

        // Determine the IdentitySource of the "Winning" classification
        // We pick the source that contributed to the best model/type
        var winnerSource = hints
            .Where(h => (bestModel != null && h.Model == bestModel) || (bestModel == null && h.Type == bestType))
            .OrderByDescending(h => MapSourceRank(h.Source))
            .FirstOrDefault()?.Source ?? "Discovery";

        return (
            bestType,
            bestModel,
            bestVendor,
            Math.Max(vendorConfidence, typeConfidence),
            MapSourceToEnum(winnerSource)
        );
    }

    /// <summary>
    /// True when OUI, pattern, or Recog hints support correcting a stale port-based type.
    /// </summary>
    public static bool ShouldAllowTypeCorrection(
        IEnumerable<ClassificationHint> hints,
        DeviceType refinedType,
        string? existingDisplayType)
    {
        if (string.IsNullOrWhiteSpace(existingDisplayType))
            return false;

        var refinedDisplay = FormatType(refinedType.ToString());
        if (refinedDisplay.Equals(existingDisplayType.Trim(), StringComparison.OrdinalIgnoreCase))
            return false;

        string[] authoritativeSources = ["OUI", "PatternLibrary", "Recog", "OpenAvcProbe"];
        return hints.Any(h =>
            authoritativeSources.Contains(h.Source)
            && h.Type == refinedType
            && h.Confidence >= 70);
    }

    private static int MapSourceRank(string source)
    {
        return source switch
        {
            "Manual" => 10,
            "SelfAwareness" => 9,
            "Recog" => 8,
            "PatternLibrary" => 7,
            "Intelligence" => 6,
            "Port" => 5,
            var s when s.StartsWith("Port:") => 5,
            "OUI" => 4,
            _ => 1
        };
    }

    private static IdentitySource MapSourceToEnum(string source)
    {
        if (source == "Manual") return IdentitySource.Manual;
        if (source == "SelfAwareness" || source == "Recog") return IdentitySource.Recog;
        if (source == "OUI") return IdentitySource.Heuristic;
        if (source == "PatternLibrary") return IdentitySource.Heuristic;
        if (source == "Intelligence" || source.StartsWith("Port:")) return IdentitySource.Intelligence;
        return IdentitySource.Discovery;
    }

    /// <summary>
    /// Helper to convert enum types to pretty display names.
    /// Underscores become spaces; camelCase tokens are split (Audio_DanteDevice → Audio Dante Device).
    /// </summary>
    public static string FormatType(string? type)
    {
        if (string.IsNullOrEmpty(type)) return "Unknown";
        var spaced = type.Replace("_", " ");
        spaced = System.Text.RegularExpressions.Regex.Replace(spaced, "([a-z])([A-Z])", "$1 $2");
        spaced = System.Text.RegularExpressions.Regex.Replace(spaced, "([A-Z]+)([A-Z][a-z])", "$1 $2");
        return System.Text.RegularExpressions.Regex.Replace(spaced, @"\s+", " ").Trim();
    }

    public static bool ShouldClearStaleProAvModel(string? existingModel, DeviceType refinedType) =>
        IsProAvModel(existingModel) && !IsProAvType(refinedType);

    private static FingerprintLibrary LoadLibrary()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = "StacksAtlas.Core.Resources.FingerprintLibrary.json";

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new Exception($"Embedded resource not found: {resourceName}");

        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();

        var options = new JsonSerializerOptions 
        { 
            PropertyNameCaseInsensitive = true 
        };
        options.Converters.Add(new JsonStringEnumConverter());

        return JsonSerializer.Deserialize<FingerprintLibrary>(json, options)
               ?? new FingerprintLibrary();
    }

    private static List<(Regex, ClassificationRule)> CompileRules(FingerprintLibrary library)
    {
        return library.Rules
            .Select(r => (new Regex(r.Pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase), r))
            .ToList();
    }

    private static DeviceType MapRecogType(string? recogType)
    {
        if (string.IsNullOrEmpty(recogType)) return DeviceType.Unknown;
        
        var lower = recogType.ToLowerInvariant();
        if (lower.Contains("switch")) return DeviceType.Network_Switch;
        if (lower.Contains("router")) return DeviceType.Network_Router;
        if (lower.Contains("ap") || lower.Contains("access point")) return DeviceType.Network_AP;
        if (lower.Contains("camera")) return DeviceType.Video_Camera;
        if (lower.Contains("ptz")) return DeviceType.Video_PTZ;
        if (lower.Contains("display") || lower.Contains("monitor") || lower.Contains("television")) return DeviceType.Video_Display;
        if (lower.Contains("projector")) return DeviceType.Video_Projector;
        if (lower.Contains("printer")) return DeviceType.Printer;
        if (lower.Contains("storage") || lower.Contains("nas")) return DeviceType.Storage_NAS;
        if (lower.Contains("laptop") || lower.Contains("notebook")) return DeviceType.Laptop;
        if (lower.Contains("receiver") || lower.Contains("avr")) return DeviceType.Audio_AVReceiver;
        if (lower.Contains("soundbar")) return DeviceType.Audio_Soundbar;
        if (lower.Contains("firewall")) return DeviceType.Network_Firewall;
        if (lower.Contains("ups")) return DeviceType.Power_UPS;
        if (lower.Contains("pdu")) return DeviceType.Power_PDU;
        if (lower.Contains("voip") || lower.Contains("sip phone")) return DeviceType.Phone_VoIP;
        if (lower.Contains("control") || lower.Contains("processor")) return DeviceType.Control_Processor;
        if (lower.Contains("touch")) return DeviceType.Control_TouchPanel;
        if (lower.Contains("phone") || lower.Contains("mobile")) return DeviceType.Mobile;
        if (lower.Contains("tablet")) return DeviceType.Tablet;
        if (lower.Contains("server")) return DeviceType.Server;
        if (lower.Contains("game") || lower.Contains("console")) return DeviceType.Gaming_Console;
        if (lower.Contains("audio") || lower.Contains("dsp")) return DeviceType.Audio_DSP;
        if (lower.Contains("mic")) return DeviceType.Audio_Microphone;
        if (lower.Contains("speaker")) return DeviceType.Audio_Speaker;
        if (lower.Contains("codec")) return DeviceType.Collaboration_Codec;
        
        return DeviceType.Unknown;
    }

    private static string NormalizeMacForOui(string mac)
    {
        var compact = mac.Replace(":", "").Replace("-", "").ToUpperInvariant();
        if (compact.Length < 6)
            return mac;

        return string.Join(":", Enumerable.Range(0, 3).Select(i => compact.Substring(i * 2, 2)));
    }

    private static bool PortRuleConflictsWithOui(
        string? portVendor,
        DeviceType portType,
        string ouiVendor,
        DeviceType ouiType)
    {
        if (string.IsNullOrWhiteSpace(portVendor) || !IsProAvPortClassification(portType, portVendor))
            return false;

        if (IsConsumerDisplayOuiVendor(ouiVendor))
            return true;

        if (ouiType is DeviceType.Video_Display or DeviceType.Media_Player or DeviceType.Video_Projector)
            return true;

        var ouiKey = ExtractVendorKey(ouiVendor);
        var portKey = ExtractVendorKey(portVendor);
        if (string.IsNullOrEmpty(ouiKey) || string.IsNullOrEmpty(portKey))
            return false;

        return !string.Equals(ouiKey, portKey, StringComparison.OrdinalIgnoreCase)
               && !ouiVendor.Contains(portVendor, StringComparison.OrdinalIgnoreCase)
               && !portVendor.Contains(ouiKey, StringComparison.OrdinalIgnoreCase);
    }

    private static void ApplyOuiConflictGuard(
        List<ClassificationHint> hints,
        ref DeviceType bestType,
        ref string? bestModel,
        ref int typeConfidence)
    {
        if (!IsProAvClassification(bestType, bestModel))
            return;

        var ouiHint = hints
            .Where(h => h.Source == "OUI" && !string.IsNullOrWhiteSpace(h.Vendor))
            .OrderByDescending(h => h.Type != DeviceType.Unknown ? 1 : 0)
            .ThenByDescending(h => h.Confidence)
            .FirstOrDefault();

        if (ouiHint == null)
            return;

        if (!IsConsumerDisplayOuiVendor(ouiHint.Vendor!)
            && ouiHint.Type == DeviceType.Unknown
            && !IsKnownProAvOuiVendor(ouiHint.Vendor!))
            return;

        if (ouiHint.Type != DeviceType.Unknown)
            bestType = ouiHint.Type;
        else if (IsConsumerDisplayOuiVendor(ouiHint.Vendor!))
            bestType = DeviceType.Video_Display;

        if (IsProAvModel(bestModel))
            bestModel = null;

        typeConfidence = Math.Max(typeConfidence, ouiHint.Confidence);
    }

    private static bool IsProAvPortClassification(DeviceType type, string? vendor) =>
        IsProAvType(type) || IsProAvVendor(vendor);

    private static bool IsProAvClassification(DeviceType type, string? model) =>
        IsProAvType(type) || IsProAvModel(model);

    private static bool IsProAvType(DeviceType type) =>
        type is DeviceType.Audio_DSP
            or DeviceType.Control_Processor
            or DeviceType.Audio_DanteDevice
            or DeviceType.Audio_Console
            or DeviceType.Audio_Interface
            or DeviceType.Audio_AVReceiver
            or DeviceType.Audio_Soundbar
            or DeviceType.Video_Matrix
            or DeviceType.Video_WallController
            or DeviceType.Lighting_Controller;

    private static bool IsProAvModel(string? model) =>
        !string.IsNullOrWhiteSpace(model)
        && (model.Contains("Q-SYS", StringComparison.OrdinalIgnoreCase)
            || model.Contains("Tesira", StringComparison.OrdinalIgnoreCase)
            || model.Contains("Crestron", StringComparison.OrdinalIgnoreCase)
            || model.Contains("Control Processor", StringComparison.OrdinalIgnoreCase));

    private static bool IsProAvVendor(string? vendor) =>
        !string.IsNullOrWhiteSpace(vendor)
        && (vendor.Contains("Q-SYS", StringComparison.OrdinalIgnoreCase)
            || vendor.Contains("QSC", StringComparison.OrdinalIgnoreCase)
            || vendor.Contains("Crestron", StringComparison.OrdinalIgnoreCase)
            || vendor.Contains("Biamp", StringComparison.OrdinalIgnoreCase)
            || vendor.Contains("AMX", StringComparison.OrdinalIgnoreCase));

    private static bool IsConsumerDisplayOuiVendor(string vendor) =>
        vendor.Contains("LG", StringComparison.OrdinalIgnoreCase)
        || vendor.Contains("Samsung", StringComparison.OrdinalIgnoreCase)
        || vendor.Contains("Sony", StringComparison.OrdinalIgnoreCase)
        || vendor.Contains("Roku", StringComparison.OrdinalIgnoreCase)
        || vendor.Contains("Vizio", StringComparison.OrdinalIgnoreCase)
        || vendor.Contains("Hisense", StringComparison.OrdinalIgnoreCase)
        || vendor.Contains("TCL", StringComparison.OrdinalIgnoreCase);

    private static bool IsKnownProAvOuiVendor(string vendor) =>
        vendor.Contains("WyreStorm", StringComparison.OrdinalIgnoreCase)
        || vendor.Contains("QSC", StringComparison.OrdinalIgnoreCase)
        || vendor.Contains("Crestron", StringComparison.OrdinalIgnoreCase)
        || vendor.Contains("Biamp", StringComparison.OrdinalIgnoreCase)
        || vendor.Contains("Extron", StringComparison.OrdinalIgnoreCase)
        || vendor.Contains("AMX", StringComparison.OrdinalIgnoreCase)
        || vendor.Contains("Shure", StringComparison.OrdinalIgnoreCase);

    private static string? ExtractVendorKey(string vendor)
    {
        if (vendor.Contains("LG", StringComparison.OrdinalIgnoreCase)) return "LG";
        if (vendor.Contains("Samsung", StringComparison.OrdinalIgnoreCase)) return "Samsung";
        if (vendor.Contains("Sony", StringComparison.OrdinalIgnoreCase)) return "Sony";
        if (vendor.Contains("WyreStorm", StringComparison.OrdinalIgnoreCase)) return "WyreStorm";
        if (vendor.Contains("QSC", StringComparison.OrdinalIgnoreCase)) return "QSC";
        if (vendor.Contains("Crestron", StringComparison.OrdinalIgnoreCase)) return "Crestron";
        if (vendor.Contains("Biamp", StringComparison.OrdinalIgnoreCase)) return "Biamp";
        return null;
    }

    // --- Internal Library Schema ---
    
    private class FingerprintLibrary
    {
        public List<ClassificationRule> Rules { get; set; } = [];
        public List<PortRule> PortRules { get; set; } = [];
    }

    private class ClassificationRule
    {
        public string Name { get; set; } = string.Empty;
        public string Pattern { get; set; } = string.Empty;
        public DeviceType Type { get; set; }
        public string? Vendor { get; set; }
        public string? Model { get; set; }
        public int Confidence { get; set; }
    }

    private class PortRule
    {
        public int Port { get; set; }
        public DeviceType Type { get; set; }
        public string? Vendor { get; set; }
        public string? Model { get; set; }
        public int Confidence { get; set; }
    }
}
