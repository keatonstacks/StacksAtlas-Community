using System;
using LiteDB;

namespace StacksAtlas.Core.Models
{
    public class LeaseHistory
    {
        [BsonId]
        public Guid Id { get; set; } = Guid.NewGuid();

        public string MacAddress { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public string? Hostname { get; set; }
        public string? Vendor { get; set; }

        public DateTime FirstSeen { get; set; }
        public DateTime LastSeen { get; set; }
    }
}
