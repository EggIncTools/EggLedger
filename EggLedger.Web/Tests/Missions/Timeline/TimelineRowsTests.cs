using EggLedger.Web.Missions.Timeline;

namespace EggLedger.Web.Tests.Missions.Timeline;

public sealed class TimelineRowsTests {
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    [Fact]
    public void Day_IsASinglePrimaryRow() {
        var start = new DateTimeOffset(2026, 8, 25, 0, 0, 0, TimeSpan.Zero);
        var end = start.AddDays(1);

        var rows = TimelineRows.Build(TimelineZoom.Day, start, end, Utc);

        Assert.Single(rows);
        Assert.True(rows[0].IsPrimary);
        Assert.Equal(start, rows[0].Start);
        Assert.Equal(end, rows[0].End);
    }

    [Fact]
    public void Week_IsASinglePrimaryRow() {
        var start = new DateTimeOffset(2026, 8, 23, 0, 0, 0, TimeSpan.Zero);
        var end = start.AddDays(7);

        var rows = TimelineRows.Build(TimelineZoom.Week, start, end, Utc);

        Assert.Single(rows);
        Assert.True(rows[0].IsPrimary);
        Assert.Equal(start, rows[0].Start);
        Assert.Equal(end, rows[0].End);
    }

    [Fact]
    public void Month_CoversTheMonthInWeekRowsStartingOnSunday() {
        var start = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

        var rows = TimelineRows.Build(TimelineZoom.Month, start, end, Utc);

        Assert.Equal(6, rows.Count);
        Assert.Equal(new DateTimeOffset(2026, 7, 26, 0, 0, 0, TimeSpan.Zero), rows[0].Start);
        Assert.All(rows, r => Assert.Equal(DayOfWeek.Sunday, r.Start.DayOfWeek));
        Assert.All(rows, r => Assert.Equal(7, (r.End - r.Start).TotalDays));
        Assert.True(rows[^1].End >= end);
    }
}
