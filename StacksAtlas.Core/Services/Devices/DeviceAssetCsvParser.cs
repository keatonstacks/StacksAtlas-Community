using System.Globalization;
using System.Text;

namespace StacksAtlas.Core.Services.Devices;

public sealed class ParsedAssetCsv
{
    public HashSet<string> AssetColumns { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<AssetCsvRow> Rows { get; init; } = [];
}

public sealed class AssetCsvRow
{
    public int LineNumber { get; init; }
    public string? DeviceId { get; init; }
    public string? NodeId { get; init; }
    public string? MacAddress { get; init; }
    public string? IpAddress { get; init; }
    public string? SerialNumber { get; init; }
    public string? AssetTag { get; init; }
    public string? FirmwareVersion { get; init; }
    public string? WarrantyExpires { get; init; }
    public bool HasSerialNumber { get; init; }
    public bool HasAssetTag { get; init; }
    public bool HasFirmwareVersion { get; init; }
    public bool HasWarrantyExpires { get; init; }
}

public static class DeviceAssetCsvParser
{
    private static readonly Dictionary<string, string> HeaderMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["name"] = "name",
        ["id"] = "deviceId",
        ["device id"] = "deviceId",
        ["deviceid"] = "deviceId",
        ["stacksatlas id"] = "deviceId",
        ["stacksatlas device id"] = "deviceId",
        ["ip address"] = "ip",
        ["ip"] = "ip",
        ["mac address"] = "mac",
        ["mac"] = "mac",
        ["node id"] = "node",
        ["node"] = "node",
        ["serial"] = "serial",
        ["serial number"] = "serial",
        ["asset tag"] = "assetTag",
        ["assettag"] = "assetTag",
        ["firmware"] = "firmware",
        ["firmware version"] = "firmware",
        ["warranty expires"] = "warranty",
        ["warranty expiry"] = "warranty",
        ["warranty expires utc"] = "warranty",
    };

    public static ParsedAssetCsv Parse(string csv)
    {
        var lines = SplitCsvRecords(csv)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();
        var result = new ParsedAssetCsv();
        if (lines.Count == 0) return result;

        var headerCells = ParseCsvLine(lines[0]);
        var columnKeys = headerCells
            .Select(NormalizeHeader)
            .ToList();

        var assetColumns = columnKeys
            .Where(k => k is "serial" or "assetTag" or "firmware" or "warranty")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        result.AssetColumns = assetColumns;

        for (var i = 1; i < lines.Count; i++)
        {
            var cells = ParseCsvLine(lines[i]);
            if (cells.All(string.IsNullOrWhiteSpace)) continue;

            string? Get(string key)
            {
                var idx = columnKeys.FindIndex(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
                return idx >= 0 && idx < cells.Count ? cells[idx] : null;
            }

            bool Has(string key) => columnKeys.Any(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));

            result.Rows.Add(new AssetCsvRow
            {
                LineNumber = i + 1,
                DeviceId = TrimOrNull(Get("deviceId")),
                NodeId = TrimOrNull(Get("node")),
                MacAddress = TrimOrNull(Get("mac")),
                IpAddress = TrimOrNull(Get("ip")),
                SerialNumber = Get("serial"),
                AssetTag = Get("assetTag"),
                FirmwareVersion = Get("firmware"),
                WarrantyExpires = Get("warranty"),
                HasSerialNumber = Has("serial"),
                HasAssetTag = Has("assetTag"),
                HasFirmwareVersion = Has("firmware"),
                HasWarrantyExpires = Has("warranty"),
            });
        }

        return result;
    }

    private static string NormalizeHeader(string raw)
    {
        var trimmed = raw.Trim().Trim('"');
        return HeaderMap.TryGetValue(trimmed, out var mapped) ? mapped : trimmed;
    }

    private static string? TrimOrNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static List<string> SplitCsvRecords(string csv)
    {
        var records = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < csv.Length; i++)
        {
            var c = csv[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < csv.Length && csv[i + 1] == '"')
                {
                    sb.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                    sb.Append(c);
                }

                continue;
            }

            if ((c == '\n' || c == '\r') && !inQuotes)
            {
                if (c == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n') i++;
                if (sb.Length > 0)
                {
                    records.Add(sb.ToString());
                    sb.Clear();
                }

                continue;
            }

            sb.Append(c);
        }

        if (sb.Length > 0) records.Add(sb.ToString());
        return records;
    }

    public static List<string> ParseLine(string line) => ParseCsvLine(line);

    private static List<string> ParseCsvLine(string line)
    {
        var cells = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            switch (ch)
            {
                case '"':
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                    break;
                case ',' when !inQuotes:
                    cells.Add(field.ToString());
                    field.Clear();
                    break;
                default:
                    field.Append(ch);
                    break;
            }
        }

        cells.Add(field.ToString());
        return cells;
    }

    public static string FormatWarrantyDate(DateTime? utc) =>
        utc.HasValue ? utc.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : string.Empty;
}
