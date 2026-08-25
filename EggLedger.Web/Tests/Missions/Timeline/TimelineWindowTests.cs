using EggLedger.Web.Missions.Timeline;

namespace EggLedger.Web.Tests.Missions.Timeline;

public sealed class TimelineWindowTests {
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;
    private static readonly TimeZoneInfo PlusFive = TimeZoneInfo.CreateCustomTimeZone("p5", TimeSpan.FromHours(5), "p5", "p5");

    [Fact]
    public void Day_SnapsToLocalMidnightBounds() {
        var center = new DateTimeOffset(2026, 8, 25, 15, 30, 0, TimeSpan.Zero);

        var (start, end) = TimelineWindow.Compute(center, TimelineZoom.Day, Utc);

        Assert.Equal(new DateTimeOffset(2026, 8, 25, 0, 0, 0, TimeSpan.Zero), start);
        Assert.Equal(new DateTimeOffset(2026, 8, 26, 0, 0, 0, TimeSpan.Zero), end);
    }

    [Fact]
    public void Day_UsesUserTimeZoneForMidnight() {
        var center = new DateTimeOffset(2026, 8, 25, 22, 0, 0, TimeSpan.Zero);

        var (start, end) = TimelineWindow.Compute(center, TimelineZoom.Day, PlusFive);

        Assert.Equal(new DateTimeOffset(2026, 8, 26, 0, 0, 0, TimeSpan.FromHours(5)), start);
        Assert.Equal(new DateTimeOffset(2026, 8, 27, 0, 0, 0, TimeSpan.FromHours(5)), end);
    }

    [Fact]
    public void Week_StartsOnSundayAndSpansSevenDays() {
        var center = new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

        var (start, end) = TimelineWindow.Compute(center, TimelineZoom.Week, Utc);

        Assert.Equal(DayOfWeek.Sunday, start.DayOfWeek);
        Assert.Equal(new DateTimeOffset(2026, 8, 23, 0, 0, 0, TimeSpan.Zero), start);
        Assert.Equal(new DateTimeOffset(2026, 8, 30, 0, 0, 0, TimeSpan.Zero), end);
    }

    [Fact]
    public void Month_SpansTheCalendarMonth() {
        var center = new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

        var (start, end) = TimelineWindow.Compute(center, TimelineZoom.Month, Utc);

        Assert.Equal(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero), start);
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), end);
    }

    [Fact]
    public void Hour_IsSixHoursAlignedToTheHour() {
        var center = new DateTimeOffset(2026, 8, 25, 14, 45, 0, TimeSpan.Zero);

        var (start, end) = TimelineWindow.Compute(center, TimelineZoom.Hour, Utc);

        Assert.Equal(new DateTimeOffset(2026, 8, 25, 11, 0, 0, TimeSpan.Zero), start);
        Assert.Equal(new DateTimeOffset(2026, 8, 25, 17, 0, 0, TimeSpan.Zero), end);
    }
}
