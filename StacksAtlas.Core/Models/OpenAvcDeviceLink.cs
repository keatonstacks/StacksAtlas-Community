using LiteDB;

namespace StacksAtlas.Core.Models;

public class OpenAvcDeviceLink
{
    [BsonId]
    public Guid StacksAtlasDeviceId { get; set; }
    public string OpenAvcDeviceId { get; set; } = "";
    public string? OpenAvcDriverId { get; set; }
    public string? OpenAvcDriverName { get; set; }
    public string? OpenAvcDeviceName { get; set; }
    public DateTime LinkedAtUtc { get; set; }
    public string? LinkedBy { get; set; }
    /// <summary>StacksAtlas MAC at link time  -  used to reattach if discovery recreates the device GUID.</summary>
    public string? StacksAtlasMacAddress { get; set; }
    public string? StacksAtlasIp { get; set; }
    /// <summary>Ordered OpenAVC macro ids pinned to this device drawer. Empty = show full project catalog.</summary>
    public List<string> PinnedMacroIds { get; set; } = [];
    /// <summary>Label → value readings captured from macro runs (serial, firmware, etc.).</summary>
    public Dictionary<string, string> ReadingsCache { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
