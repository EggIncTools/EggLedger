using EggLedger.Web.Missions.Timeline;

namespace EggLedger.Web.Tests.Missions.Timeline;

public sealed class TimelineTicksTests {
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    [Fact]
    public void HourTicks_HasHourlyLinesWithLabelsEveryThreeHours() {
        var start = new DateTimeOffset(2026, 8, 25, 0, 0, 0, TimeSpan.Zero);

        var ticks = TimelineTicks.HourTicks(start, start.AddDays(1), Utc);

        Assert.Equal(23, ticks.Count);
        Assert.Equal(7, ticks.Count(t => t.Label.Length > 0));
        Assert.Contains(ticks, t => t.Label == "3 AM");
        Assert.Contains(ticks, t => t.Label == "12 PM");
    }

    [Fact]
    public void HourTicks_PositionsAreStrictlyInsideTheWindow() {
        var start = new DateTimeOffset(2026, 8, 25, 0, 0, 0, TimeSpan.Zero);

        var ticks = TimelineTicks.HourTicks(start, start.AddDays(1), Utc);

        Assert.All(ticks, t => Assert.InRange(t.LeftPercent, 0.001, 99.999));
    }

    [Fact]
    public void DayCells_SplitsWeekRowIntoSevenEqualCells() {
        var start = new DateTimeOffset(2026, 8, 23, 0, 0, 0, TimeSpan.Zero);

        var cells = TimelineTicks.DayCells(start, start.AddDays(7), Utc, null);

        Assert.Equal(7, cells.Count);
        Assert.Equal(0, cells[0].LeftPercent, precision: 3);
        Assert.Equal(new DateTime(2026, 8, 23, 0, 0, 0, DateTimeKind.Unspecified), cells[0].LocalDate);
        Assert.All(cells, c => Assert.Equal(100.0 / 7, c.WidthPercent, precision: 3));
    }

    [Fact]
    public void DayCells_MutesDaysOutsideThePrimaryMonth() {
        var start = new DateTimeOffset(2026, 7, 26, 0, 0, 0, TimeSpan.Zero);

        var cells = TimelineTicks.DayCells(start, start.AddDays(7), Utc, 8);

        Assert.Equal(6, cells.Count(c => c.Muted));
        Assert.False(cells[6].Muted);
        Assert.Equal(1, cells[6].LocalDate.Day);
    }

    [Fact]
    public void DayCells_NoMutingWithoutAPrimaryMonth() {
        var start = new DateTimeOffset(2026, 7, 26, 0, 0, 0, TimeSpan.Zero);

        var cells = TimelineTicks.DayCells(start, start.AddDays(7), Utc, null);

        Assert.All(cells, c => Assert.False(c.Muted));
    }
}
