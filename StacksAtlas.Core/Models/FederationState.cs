using LiteDB;

namespace StacksAtlas.Core.Models;

public class FederationState
{
    [BsonId]
    public string Id { get; set; } = "singleton";
    public DateTime LastSyncUtc { get; set; } = DateTime.MinValue;
    public bool FullSyncRequired { get; set; }
    public bool BacklogMigrationComplete { get; set; }
    public DateTime? HubDisconnectedSinceUtc { get; set; }
    public DateTime? LastSuccessfulHubPushUtc { get; set; }
}
