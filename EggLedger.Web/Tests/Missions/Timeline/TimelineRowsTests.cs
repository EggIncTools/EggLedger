using EggLedger.Web.Missions.Timeline;

namespace EggLedger.Web.Tests.Missions.Timeline;

public sealed class TimelineRowsTests {
    [Fact]
    public void Day_IsASinglePrimaryRow() {
        var start = new DateTimeOffset(2026, 8, 25, 0, 0, 0, TimeSpan.Zero);
        var end = start.AddDays(1);

        var rows = TimelineRows.Build(TimelineZoom.Day, start, end);

        Assert.Single(rows);
        Assert.True(rows[0].IsPrimary);
        Assert.Equal(start, rows[0].Start);
        Assert.Equal(end, rows[0].End);
    }

    [Fact]
    public void Week_IsASinglePrimaryRow() {
        var start = new DateTimeOffset(2026, 8, 23, 0, 0, 0, TimeSpan.Zero);
        var end = start.AddDays(7);

        var rows = TimelineRows.Build(TimelineZoom.Week, start, end);

        Assert.Single(rows);
        Assert.True(rows[0].IsPrimary);
        Assert.Equal(start, rows[0].Start);
        Assert.Equal(end, rows[0].End);
    }

    [Fact]
    public void Month_CoversTheMonthInWeekRowsStartingOnSunday() {
        var start = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

        var rows = TimelineRows.Build(TimelineZoom.Month, start, end);

        Assert.Equal(6, rows.Count);
        Assert.Equal(TimelineGridAnchor.GridWeekStart(start), rows[0].Start);
        Assert.Equal(new DateTimeOffset(2026, 7, 26, 12, 0, 0, TimeSpan.FromHours(-4)), rows[0].Start);
        Assert.All(rows, r => Assert.Equal(DayOfWeek.Sunday, r.Start.DayOfWeek));
        Assert.All(rows, r => Assert.Equal(7, (r.End - r.Start).TotalDays));
        Assert.True(rows[^1].End >= end);
    }

    [Fact]
    public void Month_FirstRowStartsOnTheFirstWhenMonthBeginsOnASunday_EasternViewer() {
        var start = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.FromHours(-5));
        var end = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.FromHours(-5));

        var rows = TimelineRows.Build(TimelineZoom.Month, start, end);

        Assert.Equal(new DateOnly(2026, 2, 1), DateOnly.FromDateTime(rows[0].Start.DateTime));
    }

    [Fact]
    public void Month_FirstRowStartsOnTheFirstWhenMonthBeginsOnASunday_KolkataViewer() {
        var start = new DateTimeOffset(2026, 2, 1, 0, 0, 0, new TimeSpan(5, 30, 0));
        var end = new DateTimeOffset(2026, 3, 1, 0, 0, 0, new TimeSpan(5, 30, 0));

        var rows = TimelineRows.Build(TimelineZoom.Month, start, end);

        Assert.Equal(new DateOnly(2026, 2, 1), DateOnly.FromDateTime(rows[0].Start.DateTime));
    }

    [Fact]
    public void Month_RowBoundariesAreGridDaysWithNoGapOrOverlap() {
        var start = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

        var rows = TimelineRows.Build(TimelineZoom.Month, start, end);

        Assert.All(rows, r => Assert.Equal(TimelineGridAnchor.GridDayStart(r.Start), r.Start));
        Assert.All(rows, r => Assert.Equal(TimelineGridAnchor.GridDayStart(r.End), r.End));
        for (var i = 1; i < rows.Count; i++) {
            Assert.Equal(rows[i - 1].End, rows[i].Start);
        }
    }

    [Fact]
    public void Month_RowBoundariesAreGridDaysAcrossDstTransition() {
        var start = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);

        var rows = TimelineRows.Build(TimelineZoom.Month, start, end);

        Assert.All(rows, r => Assert.Equal(TimelineGridAnchor.GridDayStart(r.Start), r.Start));
        Assert.All(rows, r => Assert.Equal(TimelineGridAnchor.GridDayStart(r.End), r.End));
        for (var i = 1; i < rows.Count; i++) {
            Assert.Equal(rows[i - 1].End, rows[i].Start);
        }
    }
}
