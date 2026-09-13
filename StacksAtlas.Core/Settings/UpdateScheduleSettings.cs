namespace StacksAtlas.Core.Settings;

/// <summary>Appliance-local scheduled update apply (Slice 5). Portable must stay disabled.</summary>
public sealed class UpdateScheduleSettings
{
    public bool Enabled { get; set; }
    /// <summary>0 = Sunday … 6 = Saturday (local appliance time).</summary>
    public int DayOfWeek { get; set; }
    public int Hour { get; set; } = 2;
    public int Minute { get; set; }
    public string? LastAppliedVersion { get; set; }
}

/// <summary>Hub-pushed update offer waiting for Node admin confirm (Slice 4).</summary>
public sealed class PendingHubUpdateSettings
{
    public bool Active { get; set; }
    public string? Channel { get; set; }
    public string? AvailableVersion { get; set; }
    public string? InitiatedBy { get; set; }
    public DateTime? NotifiedUtc { get; set; }
    /// <summary>True when Hub used Update now (still requires local confirm unless auto-apply policy).</summary>
    public bool RequestedApply { get; set; }
    public string? Message { get; set; }
}
