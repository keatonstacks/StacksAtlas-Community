using Microsoft.Extensions.Logging;
using StacksAtlas.Core.Abstractions;

using StacksAtlas.Core.Data;

using StacksAtlas.Core.Database;

using StacksAtlas.Core.Models;

using StacksAtlas.Core.Services.Integrations;



namespace StacksAtlas.Core.Services.Devices;



public class DeviceAssetEnrichmentService(

    IDeviceRepository deviceRepository,

    OpenAvcDeviceLinkRepository linkRepository,

    OpenAvcService openAvcService,

    IClock clock,

    ILogger<DeviceAssetEnrichmentService> logger)

{

    public bool ApplyFromOpenAvcState(Device device, IReadOnlyDictionary<string, string>? state)

    {

        if (state == null || state.Count == 0)

            return false;



        var serial = DeviceAssetMetadataExtractor.ExtractSerial(state);

        var firmware = DeviceAssetMetadataExtractor.ExtractFirmware(state);

        return ApplyExtractedValues(device, serial, firmware);

    }



    public bool ApplyFromReadingsCache(Device device, IReadOnlyDictionary<string, string>? readingsCache)

    {

        if (readingsCache == null || readingsCache.Count == 0)

            return false;



        var serial = TryReadCacheLabel(readingsCache, "Serial");

        var firmware = TryReadCacheLabel(readingsCache, "Firmware");

        return ApplyExtractedValues(device, serial, firmware);

    }



    public async Task<bool> EnrichLinkedDeviceAsync(

        Guid stacksAtlasDeviceId,

        string? deviceMac = null,

        string? deviceIp = null,

        IReadOnlyDictionary<string, string>? prefetchedState = null,

        CancellationToken cancellationToken = default)

    {

        var link = linkRepository.ResolveLinkForDevice(stacksAtlasDeviceId, deviceMac, deviceIp);

        if (link == null || string.IsNullOrWhiteSpace(link.OpenAvcDeviceId))

            return false;



        var device = deviceRepository.GetById(stacksAtlasDeviceId);

        if (device == null)

            return false;



        var changed = false;

        IReadOnlyDictionary<string, string>? state = prefetchedState;

        if (state == null)

            state = await openAvcService.GetOpenAvcDeviceStateAsync(link.OpenAvcDeviceId, cancellationToken);



        if (ApplyFromOpenAvcState(device, state))

            changed = true;



        // Macros often land in readings cache (labeled Serial/Firmware) before dedicated state keys exist.

        if (link.ReadingsCache is { Count: > 0 } cache && ApplyFromReadingsCache(device, cache))

            changed = true;



        return changed;

    }



    private bool ApplyExtractedValues(Device device, string? serial, string? firmware)

    {

        var changed = false;



        if (!device.IsSerialNumberManuallySet

            && !string.IsNullOrWhiteSpace(serial)

            && !string.Equals(device.SerialNumber, serial, StringComparison.Ordinal))

        {

            device.SerialNumber = serial;

            device.AssetMetadataSource = AssetMetadataSource.OpenAvc;

            changed = true;

        }



        if (!device.IsFirmwareVersionManuallySet

            && !string.IsNullOrWhiteSpace(firmware)

            && !string.Equals(device.FirmwareVersion, firmware, StringComparison.Ordinal))

        {

            device.FirmwareVersion = firmware;

            device.AssetMetadataSource = AssetMetadataSource.OpenAvc;

            changed = true;

        }



        if (!changed)

            return false;



        device.LastModifiedUtc = clock.UtcNow;

        deviceRepository.UpsertDevice(device, true);

        logger.LogDebug(

            "OpenAVC asset enrichment updated device {DeviceId} (serial={HasSerial}, firmware={HasFirmware})",

            device.Id,

            !string.IsNullOrWhiteSpace(device.SerialNumber),

            !string.IsNullOrWhiteSpace(device.FirmwareVersion));

        return true;

    }



    private static string? TryReadCacheLabel(IReadOnlyDictionary<string, string> cache, string label)

    {

        foreach (var pair in cache)

        {

            if (string.Equals(pair.Key, label, StringComparison.OrdinalIgnoreCase)

                && !string.IsNullOrWhiteSpace(pair.Value))

            {

                return pair.Value.Trim();

            }

        }



        return null;

    }

}


