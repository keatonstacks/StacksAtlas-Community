using LiteDB;
using Microsoft.Extensions.Logging;
using StacksAtlas.Core.Models;

namespace StacksAtlas.Core.Database;

public class OpenAvcDeviceLinkRepository(LiteDatabase db, ILogger<OpenAvcDeviceLinkRepository> logger)
{
    private readonly ILiteCollection<OpenAvcDeviceLink> _collection = db.GetCollection<OpenAvcDeviceLink>("openavc_device_links");

    public OpenAvcDeviceLink? GetByDeviceId(Guid stacksAtlasDeviceId)
    {
        try
        {
            return _collection.FindById(stacksAtlasDeviceId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load OpenAVC link for device {DeviceId}", stacksAtlasDeviceId);
            return null;
        }
    }

    public OpenAvcDeviceLink? GetByMacAddress(string? macAddress)
    {
        var mac = NormalizeMac(macAddress);
        if (string.IsNullOrEmpty(mac))
            return null;
        try
        {
            return _collection.FindOne(x => x.StacksAtlasMacAddress == mac);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load OpenAVC link for MAC {Mac}", mac);
            return null;
        }
    }

    /// <summary>
    /// Resolve link by device id, or reattach an existing MAC-keyed link when discovery recreated the GUID.
    /// </summary>
    public OpenAvcDeviceLink? ResolveLinkForDevice(Guid deviceId, string? macAddress, string? ipAddress)
    {
        var direct = GetByDeviceId(deviceId);
        if (direct != null)
            return direct;

        var byMac = GetByMacAddress(macAddress);
        if (byMac == null)
            return null;

        if (byMac.StacksAtlasDeviceId == deviceId)
            return byMac;

        try
        {
            var oldId = byMac.StacksAtlasDeviceId;
            Delete(oldId);
            byMac.StacksAtlasDeviceId = deviceId;
            if (!string.IsNullOrWhiteSpace(ipAddress))
                byMac.StacksAtlasIp = ipAddress.Trim();
            Upsert(byMac);
            logger.LogInformation(
                "OpenAVC link migrated from device {OldId} to {NewId} via MAC {Mac}",
                oldId,
                deviceId,
                macAddress);
            return byMac;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to migrate OpenAVC link to device {DeviceId}", deviceId);
            return byMac;
        }
    }

    public void Upsert(OpenAvcDeviceLink link)
    {
        try
        {
            _collection.Upsert(link.StacksAtlasDeviceId, link);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save OpenAVC link for device {DeviceId}", link.StacksAtlasDeviceId);
            throw;
        }
    }

    public bool Delete(Guid stacksAtlasDeviceId)
    {
        try
        {
            return _collection.Delete(stacksAtlasDeviceId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete OpenAVC link for device {DeviceId}", stacksAtlasDeviceId);
            return false;
        }
    }

    public List<OpenAvcDeviceLink> GetAll()
    {
        try
        {
            return _collection.FindAll().ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to list OpenAVC device links.");
            return [];
        }
    }

    internal static string NormalizeMac(string? macAddress)
    {
        if (string.IsNullOrWhiteSpace(macAddress))
            return "";
        return macAddress.Replace(":", "").Replace("-", "").Replace(".", "").ToUpperInvariant();
    }
}
