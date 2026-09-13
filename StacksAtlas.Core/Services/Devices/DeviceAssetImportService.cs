using Microsoft.Extensions.Logging;
using StacksAtlas.Core.Data;
using StacksAtlas.Core.Models;

namespace StacksAtlas.Core.Services.Devices;

public interface IDeviceAssetImportService
{
    DeviceAssetImportResult ImportCsv(string csv);
    DeviceAssetImportResult ImportRows(IReadOnlyList<AssetCsvRow> rows, IReadOnlySet<string>? assetColumns = null);
}

public class DeviceAssetImportService(
    IDeviceRepository repo,
    ILogger<DeviceAssetImportService> logger) : IDeviceAssetImportService
{
    private readonly IDeviceRepository _repo = repo;
    private readonly ILogger<DeviceAssetImportService> _logger = logger;

    public DeviceAssetImportResult ImportCsv(string csv)
    {
        var parsed = DeviceAssetCsvParser.Parse(csv);
        if (parsed.Rows.Count == 0)
        {
            return new DeviceAssetImportResult
            {
                Failed = 1,
                Errors = ["CSV has no data rows."],
            };
        }

        if (parsed.AssetColumns.Count == 0)
        {
            return new DeviceAssetImportResult
            {
                Failed = 1,
                Errors = ["CSV must include at least one asset column (Serial, Asset Tag, Firmware, or Warranty Expires)."],
            };
        }

        return ImportRows(parsed.Rows, parsed.AssetColumns);
    }

    public DeviceAssetImportResult ImportRows(IReadOnlyList<AssetCsvRow> rows, IReadOnlySet<string>? assetColumns = null)
    {
        var result = new DeviceAssetImportResult();
        var devices = _repo.GetAll();

        foreach (var row in rows)
        {
            if (!HasMatchKey(row))
            {
                result.Failed++;
                result.Errors.Add($"Row {row.LineNumber}: Device ID, MAC Address, or IP Address is required.");
                continue;
            }

            var device = FindDevice(devices, row);
            if (device == null)
            {
                result.NotFound++;
                var key = row.DeviceId ?? row.MacAddress ?? row.IpAddress ?? "?";
                result.Errors.Add($"Row {row.LineNumber}: No device matched ({key}).");
                continue;
            }

            string? serial = row.HasSerialNumber ? row.SerialNumber ?? string.Empty : null;
            string? assetTag = row.HasAssetTag ? row.AssetTag ?? string.Empty : null;
            string? firmware = row.HasFirmwareVersion ? row.FirmwareVersion ?? string.Empty : null;
            string? warranty = row.HasWarrantyExpires ? row.WarrantyExpires ?? string.Empty : null;

            if (serial == null && assetTag == null && firmware == null && warranty == null)
            {
                result.Failed++;
                result.Errors.Add($"Row {row.LineNumber}: No asset fields to update.");
                continue;
            }

            var (error, skippedOpenAvc, anyApplied) = DeviceAssetMetadataPatcher.TryApplyImport(
                device, serial, assetTag, firmware, warranty);
            result.SkippedOpenAvc += skippedOpenAvc;

            if (error != null)
            {
                result.Failed++;
                result.Errors.Add($"Row {row.LineNumber}: {error}");
                continue;
            }

            if (!anyApplied)
                continue;

            try
            {
                _repo.UpsertDevice(device, isUserAction: true);
                result.Updated++;
                result.UpdatedDeviceIds.Add(device.Id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Asset import failed for device {DeviceId}", device.Id);
                result.Failed++;
                result.Errors.Add($"Row {row.LineNumber}: Save failed.");
            }
        }

        return result;
    }

    private static bool HasMatchKey(AssetCsvRow row) =>
        !string.IsNullOrWhiteSpace(row.DeviceId)
        || !string.IsNullOrWhiteSpace(row.MacAddress)
        || !string.IsNullOrWhiteSpace(row.IpAddress);

    private static Device? FindDevice(List<Device> devices, AssetCsvRow row)
    {
        if (!string.IsNullOrWhiteSpace(row.DeviceId))
        {
            if (!Guid.TryParse(row.DeviceId.Trim(), out var guid))
                return null;

            return devices.FirstOrDefault(d => d.Id == guid);
        }

        IEnumerable<Device> query = devices;

        if (!string.IsNullOrWhiteSpace(row.NodeId))
        {
            query = query.Where(d => string.Equals(d.NodeId, row.NodeId.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        var list = query.ToList();

        if (!string.IsNullOrWhiteSpace(row.MacAddress))
        {
            var norm = DeviceMacNormalizer.Normalize(row.MacAddress);
            if (!string.IsNullOrEmpty(norm))
            {
                var byMac = list.FirstOrDefault(d =>
                    string.Equals(DeviceMacNormalizer.Normalize(d.MacAddress), norm, StringComparison.Ordinal));
                if (byMac != null) return byMac;
            }
        }

        if (!string.IsNullOrWhiteSpace(row.IpAddress))
        {
            return list.FirstOrDefault(d =>
                string.Equals(d.IpAddress, row.IpAddress.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }
}
