using StacksAtlas.Core.Models;

namespace StacksAtlas.Core.Services.Devices;

/// <summary>Shared apply logic for manual asset field updates (API PATCH and CSV import).</summary>
public static class DeviceAssetMetadataPatcher
{
    public const int MaxFieldLength = 128;

    public static bool IsOpenAvcProtectedSerial(Device device) =>
        !device.IsSerialNumberManuallySet
        && device.AssetMetadataSource == AssetMetadataSource.OpenAvc
        && !string.IsNullOrWhiteSpace(device.SerialNumber);

    public static bool IsOpenAvcProtectedFirmware(Device device) =>
        !device.IsFirmwareVersionManuallySet
        && device.AssetMetadataSource == AssetMetadataSource.OpenAvc
        && !string.IsNullOrWhiteSpace(device.FirmwareVersion);

    /// <summary>Drawer / PATCH  -  always honors user intent.</summary>
    public static string? TryApply(
        Device device,
        string? serialNumber,
        string? assetTag,
        string? firmwareVersion,
        string? warrantyExpiresUtc)
    {
        if (serialNumber != null)
        {
            if (serialNumber.Length > MaxFieldLength)
                return "Serial number must be 128 characters or fewer.";

            if (string.IsNullOrWhiteSpace(serialNumber))
            {
                device.SerialNumber = null;
                device.IsSerialNumberManuallySet = false;
            }
            else
            {
                device.SerialNumber = serialNumber.Trim();
                device.IsSerialNumberManuallySet = true;
                device.AssetMetadataSource = AssetMetadataSource.Manual;
            }
        }

        if (assetTag != null)
        {
            if (assetTag.Length > MaxFieldLength)
                return "Asset tag must be 128 characters or fewer.";

            device.AssetTag = string.IsNullOrWhiteSpace(assetTag) ? null : assetTag.Trim();
            device.IsAssetTagManuallySet = true;
            if (!string.IsNullOrWhiteSpace(device.AssetTag))
                device.AssetMetadataSource = AssetMetadataSource.Manual;
        }

        if (firmwareVersion != null)
        {
            if (firmwareVersion.Length > MaxFieldLength)
                return "Firmware version must be 128 characters or fewer.";

            if (string.IsNullOrWhiteSpace(firmwareVersion))
            {
                device.FirmwareVersion = null;
                device.IsFirmwareVersionManuallySet = false;
            }
            else
            {
                device.FirmwareVersion = firmwareVersion.Trim();
                device.IsFirmwareVersionManuallySet = true;
                device.AssetMetadataSource = AssetMetadataSource.Manual;
            }
        }

        if (warrantyExpiresUtc != null)
        {
            if (string.IsNullOrWhiteSpace(warrantyExpiresUtc))
            {
                device.WarrantyExpiresUtc = null;
            }
            else if (DateTime.TryParse(warrantyExpiresUtc, out var warrantyDate))
            {
                device.WarrantyExpiresUtc = warrantyDate.Date;
            }
            else
            {
                return "Invalid warranty expiry date.";
            }

            device.IsWarrantyExpiresManuallySet = true;
            device.AssetMetadataSource = AssetMetadataSource.Manual;
        }

        return null;
    }

    /// <summary>CSV import  -  skips OpenAVC-enriched serial/firmware when not manually set.</summary>
    public static (string? Error, int SkippedOpenAvc, bool AnyApplied) TryApplyImport(
        Device device,
        string? serialNumber,
        string? assetTag,
        string? firmwareVersion,
        string? warrantyExpiresUtc)
    {
        var skipped = 0;

        if (serialNumber != null && IsOpenAvcProtectedSerial(device))
        {
            serialNumber = null;
            skipped++;
        }

        if (firmwareVersion != null && IsOpenAvcProtectedFirmware(device))
        {
            firmwareVersion = null;
            skipped++;
        }

        if (serialNumber == null && assetTag == null && firmwareVersion == null && warrantyExpiresUtc == null)
            return (null, skipped, false);

        var error = TryApply(device, serialNumber, assetTag, firmwareVersion, warrantyExpiresUtc);
        return (error, skipped, error == null);
    }

    /// <summary>Clears site-entered serial/firmware only (leaves OpenAVC-enriched values).</summary>
    public static bool TryClearManualSerialAndFirmware(Device device)
    {
        var changed = false;

        if (device.IsSerialNumberManuallySet)
        {
            device.SerialNumber = null;
            device.IsSerialNumberManuallySet = false;
            changed = true;
        }

        if (device.IsFirmwareVersionManuallySet)
        {
            device.FirmwareVersion = null;
            device.IsFirmwareVersionManuallySet = false;
            changed = true;
        }

        return changed;
    }
}
