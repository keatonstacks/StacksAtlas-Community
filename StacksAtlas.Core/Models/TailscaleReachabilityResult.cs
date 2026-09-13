namespace StacksAtlas.Core.Models;

public class TailscaleReachabilityProbe
{
    public required string Label { get; set; }
    public required string Host { get; set; }
    public int Port { get; set; }
    public bool Reachable { get; set; }
    public int? LatencyMs { get; set; }
    public string? Error { get; set; }
}

public class TailscaleReachabilityResult
{
    public bool AnyReachable { get; set; }
    public string Summary { get; set; } = string.Empty;
    public List<TailscaleReachabilityProbe> Probes { get; set; } = [];
}
