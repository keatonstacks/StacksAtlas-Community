using StacksAtlas.Core.Abstractions;
using StacksAtlas.Core.Settings;
using StacksAtlas.Core.State;

namespace StacksAtlas.Core.Services.Updates;

/// <summary>Pure helpers for Hub pending-update offers and maintenance-window evaluation.</summary>
public static class UpdateFleetPolicy
{
    public const int MaintenanceWindowMinutes = 30;

    /// <summary>
    /// True when <paramref name="availableVersion"/> is a newer semver than <paramref name="currentVersion"/>.
    /// Unknown/unparsable current allows the offer; unparsable available rejects it.
    /// </summary>
    public static bool IsActionableOffer(string? availableVersion, string? currentVersion)
    {
        if (string.IsNullOrWhiteSpace(availableVersion))
            return false;

        if (string.IsNullOrWhiteSpace(currentVersion))
            return true;

        if (!SemverUtility.TryParse(availableVersion, out _) || !SemverUtility.TryParse(currentVersion, out _))
        {
            return !string.Equals(
                availableVersion.Trim(),
                currentVersion.Trim(),
                StringComparison.OrdinalIgnoreCase);
        }

        return SemverUtility.IsNewerThan(availableVersion, currentVersion);
    }

    public static void SetPendingHubUpdate(
        SystemSettingsStore store,
        string channel,
        string availableVersion,
        string? initiatedBy,
        bool requestedApply,
        string? message,
        IClock clock)
    {
        var settings = store.Load();
        settings.PendingHubUpdate = new PendingHubUpdateSettings
        {
            Active = true,
            Channel = UpdateDepotPaths.NormalizeChannel(channel),
            AvailableVersion = availableVersion.Trim(),
            InitiatedBy = string.IsNullOrWhiteSpace(initiatedBy) ? null : initiatedBy.Trim(),
            NotifiedUtc = clock.UtcNow,
            RequestedApply = requestedApply,
            Message = message
        };
        store.Save(settings);
    }

    public static void ClearPendingHubUpdate(SystemSettingsStore store)
    {
        var settings = store.Load();
        if (!settings.PendingHubUpdate.Active && settings.PendingHubUpdate.AvailableVersion is null)
            return;

        settings.PendingHubUpdate = new PendingHubUpdateSettings();
        store.Save(settings);
    }

    /// <summary>
    /// Clears a stale Hub offer when the appliance is already on/newer than the offered version.
    /// Returns the pending record after any clear (inactive when cleared).
    /// </summary>
    public static PendingHubUpdateSettings ResolvePendingHubUpdate(
        SystemSettingsStore store,
        string? currentVersion)
    {
        var pending = store.Load().PendingHubUpdate ?? new PendingHubUpdateSettings();
        if (!pending.Active)
            return pending;

        if (IsActionableOffer(pending.AvailableVersion, currentVersion))
            return pending;

        ClearPendingHubUpdate(store);
        return new PendingHubUpdateSettings();
    }

    public static bool IsInMaintenanceWindow(UpdateScheduleSettings schedule, DateTime localNow)
    {
        if (!schedule.Enabled)
            return false;

        var day = (int)localNow.DayOfWeek;
        if (day != schedule.DayOfWeek)
            return false;

        var start = schedule.Hour * 60 + schedule.Minute;
        var current = localNow.Hour * 60 + localNow.Minute;
        return current >= start && current < start + MaintenanceWindowMinutes;
    }

    public static bool ShouldAutoApply(
        UpdateScheduleSettings schedule,
        string? availableVersion,
        DateTime localNow,
        bool isPortable)
    {
        if (isPortable || ExecutionState.IsPortable)
            return false;
        if (!schedule.Enabled || string.IsNullOrWhiteSpace(availableVersion))
            return false;
        if (string.Equals(schedule.LastAppliedVersion, availableVersion, StringComparison.OrdinalIgnoreCase))
            return false;
        return IsInMaintenanceWindow(schedule, localNow);
    }

    public static UpdateScheduleSettings NormalizeSchedule(UpdateScheduleSettings? input)
    {
        var s = input ?? new UpdateScheduleSettings();
        return new UpdateScheduleSettings
        {
            Enabled = s.Enabled,
            DayOfWeek = Math.Clamp(s.DayOfWeek, 0, 6),
            Hour = Math.Clamp(s.Hour, 0, 23),
            Minute = Math.Clamp(s.Minute, 0, 59),
            LastAppliedVersion = s.LastAppliedVersion
        };
    }
}
