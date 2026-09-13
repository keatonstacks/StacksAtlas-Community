using LiteDB;

namespace StacksAtlas.Core.Models
{
    public class SystemEvent
    {
        [BsonId]
        public ObjectId Id { get; set; } = ObjectId.Empty;

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        // "DeviceOnline", "DeviceOffline", "NewDevice", "SubnetUnreachable", etc.
        public string Type { get; set; } = string.Empty;

        // Human-readable message for UI
        public string Message { get; set; } = string.Empty;

        // Optional: link event to a device
        public string? DeviceIp { get; set; }

        // Optional: link event to a subnet
        public string? Subnet { get; set; }

        // "Info", "Warning", "Critical"
        public string Severity { get; set; } = "Info";

        public string? NodeId { get; set; }

        public string? NodeName { get; set; }
        public string? Client { get; set; }
        public string? Building { get; set; }
        public string? Room { get; set; }
    }
}
