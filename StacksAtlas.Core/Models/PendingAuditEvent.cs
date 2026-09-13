using LiteDB;

namespace StacksAtlas.Core.Models;

public class PendingAuditEvent
{
    [BsonId]
    public ObjectId Id { get; set; } = ObjectId.NewObjectId();

    public AuditEvent Event { get; set; } = null!;

    public DateTime CreatedAtUtc { get; set; }
}
