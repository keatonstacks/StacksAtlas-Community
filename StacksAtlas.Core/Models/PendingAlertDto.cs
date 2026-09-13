using System;

namespace StacksAtlas.Core.Models
{
    public class PendingAlertDto
    {
        public string Id { get; set; } = string.Empty;
        public AlertEvent Alert { get; set; } = null!;
        public DeviceDto? Device { get; set; }
        public bool IsDelegation { get; set; }
        public bool IsHistoricalReplay { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }

    public class DeviceDto
    {
        public Guid Id { get; set; }
        public string IpAddress { get; set; } = string.Empty;
        public string? MacAddress { get; set; }
        public string Name { get; set; } = string.Empty;
        public Guid? ManagedByUserId { get; set; }
        public bool AlertsEnabled { get; set; } = true;
        public string NodeId { get; set; } = string.Empty;
        public DateTime? FirstDiscoveredUtc { get; set; }
    }
}
