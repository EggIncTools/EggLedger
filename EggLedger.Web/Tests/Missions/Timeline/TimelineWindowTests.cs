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
    public void Week_StartsOnGridSundayAndSpansSevenDays() {
        var center = new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

        var (start, end) = TimelineWindow.Compute(center, TimelineZoom.Week, Utc);

        Assert.Equal(DayOfWeek.Sunday, start.DayOfWeek);
        Assert.Equal(TimelineGridAnchor.GridWeekStart(center), start);
        Assert.Equal(new DateTimeOffset(2026, 8, 23, 12, 0, 0, TimeSpan.FromHours(-4)), start);
        Assert.Equal(new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.FromHours(-4)), end);
    }

    [Fact]
    public void Week_AnchorsToEasternNoonNotViewerLocalMidnight() {
        var center = new DateTimeOffset(2026, 8, 25, 3, 0, 0, TimeSpan.Zero);

        var (start, _) = TimelineWindow.Compute(center, TimelineZoom.Week, Utc);

        Assert.NotEqual(new DateTimeOffset(2026, 8, 23, 0, 0, 0, TimeSpan.Zero), start);
        Assert.Equal(TimelineGridAnchor.GridDayStart(start), start);
        Assert.Equal(DayOfWeek.Sunday, TimeZoneInfo.ConvertTime(start, TimeZoneInfo.FindSystemTimeZoneById("America/New_York")).DayOfWeek);
    }

    [Fact]
    public void Week_IsUnaffectedByViewerTimeZoneChoice() {
        var center = new DateTimeOffset(2026, 8, 25, 3, 0, 0, TimeSpan.Zero);

        var (utcStart, utcEnd) = TimelineWindow.Compute(center, TimelineZoom.Week, Utc);
        var (plusFiveStart, plusFiveEnd) = TimelineWindow.Compute(center, TimelineZoom.Week, PlusFive);

        Assert.Equal(utcStart, plusFiveStart);
        Assert.Equal(utcEnd, plusFiveEnd);
    }

    [Fact]
    public void Month_SpansTheCalendarMonth() {
        var center = new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

        var (start, end) = TimelineWindow.Compute(center, TimelineZoom.Month, Utc);

        Assert.Equal(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero), start);
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), end);
    }

}
