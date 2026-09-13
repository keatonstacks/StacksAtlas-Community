namespace StacksAtlas.Core.Models
{
    public class MaintenanceState
    {
        public int Id { get; set; } = 1;

        public DateTime LastCleanupUtc { get; set; }
        public DateTime NextCleanupUtc { get; set; }

        public DateTime LastCompactUtc { get; set; }
        public DateTime NextCompactUtc { get; set; }

        // Metrics
        public int LastPurgedCount { get; set; }
        public long LastDatabaseSize { get; set; }
        
        // Status
        public string CurrentStatus { get; set; } = "Idle";
        public string? LastErrorMessage { get; set; }
    }
}
