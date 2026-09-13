namespace StacksAtlas.Core.Models
{
    public class SweepHistoryEntry
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        public long DurationMs { get; set; }

        public int TotalOnline { get; set; }
        public int TotalHosts { get; set; }

        public List<SubnetScanResult> Subnets { get; set; } = [];

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
