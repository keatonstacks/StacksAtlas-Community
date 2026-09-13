namespace StacksAtlas.Core.Models
{
    public record SweepResult(
        DateTime Start,
        DateTime End,
        long DurationMs,
        int TotalOnline,
        int TotalHosts,
        List<SubnetScanResult> Subnets
    )
    {
        public int TotalOffline => TotalHosts - TotalOnline;

        public string ToSummary() =>
            $"Sweep: {TotalOnline}/{TotalHosts} online, Duration={DurationMs} ms";
    }
}
