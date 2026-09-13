using System;
using LiteDB;

namespace StacksAtlas.Core.Models;

public class PendingEvent
{
    [BsonId]
    public ObjectId Id { get; set; } = ObjectId.NewObjectId();
    public SystemEvent Event { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; }
}
