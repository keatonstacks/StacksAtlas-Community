using LiteDB;

namespace StacksAtlas.Core.Models;

/// <summary>
/// Fast lookup registry for permanently removed device identities (§7.9 Phase B).
/// Keyed by { NodeId, NormalizedMac }  -  scope-agnostic governance.
/// </summary>
public class DeviceSuppression
{
    [BsonId]
    public string Id { get; set; } = string.Empty;

    public string NodeId { get; set; } = string.Empty;
    public string NormalizedMac { get; set; } = string.Empty;
    public Guid DeviceId { get; set; }
    public DateTime SuppressedUtc { get; set; }
    public string? RemovedBy { get; set; }
}
