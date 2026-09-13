namespace StacksAtlas.Core.Models.Updates;

/// <summary>Machine-readable outcome for update check UI routing.</summary>
public static class UpdateCheckStatuses
{
    public const string UpToDate = "upToDate";
    public const string UpdateAvailable = "updateAvailable";
    public const string Unavailable = "unavailable";
    public const string CheckFailed = "checkFailed";
}
