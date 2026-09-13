using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StacksAtlas.Core.Models
{
    public static class OuiDatabase
    {
        private static readonly Dictionary<string, string> _vendors;

        static OuiDatabase()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "StacksAtlas.Core.Resources.oui.json";

            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new Exception($"Embedded resource not found: {resourceName}");

            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();

            // Deserialize the array of raw entries
            var entries = JsonSerializer.Deserialize<List<RawOui>>(json)
                          ?? [];

            // C# 13 collection expression
            _vendors = [];

            foreach (var e in entries)
            {
                if (string.IsNullOrWhiteSpace(e.Assignment) ||
                    string.IsNullOrWhiteSpace(e.OrganizationName))
                    continue;

                // Convert "08EA44" ? "08:EA:44"
                if (e.Assignment.Length >= 6)
                {
                    var prefix = string.Join(":", Enumerable.Range(0, 3)
                        .Select(i => e.Assignment.Substring(i * 2, 2).ToUpper()));

                    if (!_vendors.ContainsKey(prefix))
                        _vendors[prefix] = e.OrganizationName.Trim();
                }
            }
        }

        private static readonly Dictionary<string, DeviceType> _vendorTypeHints = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Apple", DeviceType.Mobile },
            { "Cisco", DeviceType.Network_Infrastructure },
            { "Ubiquiti", DeviceType.Network_Infrastructure },
            { "Crestron", DeviceType.Control_Processor },
            { "QSC", DeviceType.Audio_DSP },
            { "Biamp", DeviceType.Audio_DSP },
            { "Shure", DeviceType.Audio_Microphone },
            { "Axis", DeviceType.Video_Camera },
            { "Hikvision", DeviceType.Video_NVR },
            { "Dahua", DeviceType.Video_NVR },
            { "Synology", DeviceType.Storage_NAS },
            { "QNAP", DeviceType.Storage_NAS },
            { "HP", DeviceType.Printer },
            { "Brother", DeviceType.Printer },
            { "Epson", DeviceType.Printer },
            { "Canon", DeviceType.Printer },
            { "Microsoft", DeviceType.Computer },
            { "Dell", DeviceType.Computer },
            { "Sony", DeviceType.Media_Player },
            { "Samsung", DeviceType.Video_Display },
            { "LG", DeviceType.Video_Display },
            { "Pioneer", DeviceType.Media_Player },
            { "WyreStorm", DeviceType.Video_Decoder },
        };

        public static (string Vendor, DeviceType Hint) Lookup(string? mac)
        {
            if (string.IsNullOrWhiteSpace(mac))
                return ("Unknown Vendor", DeviceType.Unknown);

            mac = mac.ToUpper().Replace("-", ":");

            var parts = mac.Split(':');
            if (parts.Length < 3)
                return ("Unknown Vendor", DeviceType.Unknown);

            var prefix = $"{parts[0]}:{parts[1]}:{parts[2]}";

            if (_vendors.TryGetValue(prefix, out var vendor))
            {
                // Find a type hint based on the organization name
                var hint = _vendorTypeHints.FirstOrDefault(x => vendor.Contains(x.Key, StringComparison.OrdinalIgnoreCase)).Value;
                return (vendor, hint != default ? hint : DeviceType.Unknown);
            }

            return ("Unknown Vendor", DeviceType.Unknown);
        }

        private class RawOui
        {
            [JsonPropertyName("Registry")]
            public string? Registry { get; set; }

            [JsonPropertyName("Assignment")]
            public string? Assignment { get; set; }

            [JsonPropertyName("Organization Name")]
            public string? OrganizationName { get; set; }

            [JsonPropertyName("Organization Address")]
            public string? OrganizationAddress { get; set; }
        }
    }
}
