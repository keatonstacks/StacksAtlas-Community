using StacksAtlas.Core.Abstractions;
using StacksAtlas.Core.Services.Updates;
using StacksAtlas.Core.Settings;

namespace StacksAtlas.Tests;

public sealed class UpdateFleetPolicyTests
{
    private sealed class FixedClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow => utcNow;
        public DateTime Now => utcNow.ToLocalTime();
    }

    [Theory]
    [InlineData(2, 0, 2, 0, true)]
    [InlineData(2, 0, 2, 29, true)]
    [InlineData(2, 0, 2, 30, false)]
    [InlineData(2, 0, 1, 59, false)]
    public void IsInMaintenanceWindow_30_minute_window(int hour, int minute, int nowHour, int nowMinute, bool expected)
    {
        var schedule = new UpdateScheduleSettings
        {
            Enabled = true,
            DayOfWeek = (int)DayOfWeek.Wednesday,
            Hour = hour,
            Minute = minute
        };
        var now = new DateTime(2026, 7, 8, nowHour, nowMinute, 0); // Wednesday
        Assert.Equal(expected, UpdateFleetPolicy.IsInMaintenanceWindow(schedule, now));
    }

    [Fact]
    public void ShouldAutoApply_rejects_portable_and_already_applied()
    {
        var schedule = new UpdateScheduleSettings
        {
            Enabled = true,
            DayOfWeek = (int)DayOfWeek.Wednesday,
            Hour = 2,
            Minute = 0,
            LastAppliedVersion = "1.9.2"
        };
        var now = new DateTime(2026, 7, 8, 2, 10, 0);

        Assert.False(UpdateFleetPolicy.ShouldAutoApply(schedule, "1.9.2", now, isPortable: false));
        Assert.False(UpdateFleetPolicy.ShouldAutoApply(schedule, "1.9.3", now, isPortable: true));

        schedule.LastAppliedVersion = null;
        Assert.True(UpdateFleetPolicy.ShouldAutoApply(schedule, "1.9.3", now, isPortable: false));
    }

    [Fact]
    public void NormalizeSchedule_clamps_fields()
    {
        var normalized = UpdateFleetPolicy.NormalizeSchedule(new UpdateScheduleSettings
        {
            Enabled = true,
            DayOfWeek = 99,
            Hour = 40,
            Minute = 99
        });
        Assert.Equal(6, normalized.DayOfWeek);
        Assert.Equal(23, normalized.Hour);
        Assert.Equal(59, normalized.Minute);
    }

    [Fact]
    public void IsActionableOffer_requires_newer_semver()
    {
        Assert.False(UpdateFleetPolicy.IsActionableOffer("1.9.2", "1.9.2"));
        Assert.False(UpdateFleetPolicy.IsActionableOffer("1.9.1", "1.9.2"));
        Assert.True(UpdateFleetPolicy.IsActionableOffer("1.9.3", "1.9.2"));
        Assert.True(UpdateFleetPolicy.IsActionableOffer("1.9.3", null));
        Assert.False(UpdateFleetPolicy.IsActionableOffer(null, "1.9.2"));
        Assert.False(UpdateFleetPolicy.IsActionableOffer(" ", "1.9.2"));
    }
}
