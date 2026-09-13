using System;
using LiteDB;

namespace StacksAtlas.Core.Models;

public class PendingAlert
{
    [BsonId]
    public ObjectId Id { get; set; } = ObjectId.NewObjectId();
    public AlertEvent Alert { get; set; } = null!;
    public Device? Device { get; set; }
    public bool IsDelegation { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
