using LiteDB;

namespace StacksAtlas.Core.Models;

public class PendingTelemetry
{
    [BsonId]
    public Guid Id { get; set; } = Guid.NewGuid();
    public List<Device> Devices { get; set; } = [];
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
