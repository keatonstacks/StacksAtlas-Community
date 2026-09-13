namespace StacksAtlas.Core.Settings;

/// <summary>
/// Persisted across Hub-initiated site reset (survives LiteDB wipe in systemsettings.json).
/// </summary>
public sealed class SiteResetRecoveryContext
{
    public bool HubInitiated { get; set; }
    public string? InitiatedByUsername { get; set; }
    public DateTime? InitiatedAtUtc { get; set; }
    public string? HubDisplayName { get; set; }
}
