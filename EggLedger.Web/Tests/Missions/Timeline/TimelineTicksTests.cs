using EggLedger.Web.Missions.Timeline;

namespace EggLedger.Web.Tests.Missions.Timeline;

public sealed class TimelineTicksTests {
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    [Fact]
    public void Week_HasSixInteriorGridLinesAndSevenDayLabels() {
        var start = new DateTimeOffset(2026, 8, 23, 0, 0, 0, TimeSpan.Zero);

        var ticks = TimelineTicks.Generate(start, start.AddDays(7), TimelineZoom.Week, Utc);

        Assert.Equal(6, ticks.Count(t => t.HasLine));
        Assert.Equal(7, ticks.Count(t => t.Label.Length > 0));
        Assert.Contains(ticks, t => t.Label == "Sun 8/23");
    }

    [Fact]
    public void Day_HasHourlyLinesWithLabelsEveryThreeHours() {
        var start = new DateTimeOffset(2026, 8, 25, 0, 0, 0, TimeSpan.Zero);

        var ticks = TimelineTicks.Generate(start, start.AddDays(1), TimelineZoom.Day, Utc);

        Assert.Equal(23, ticks.Count(t => t.HasLine));
        Assert.Equal(7, ticks.Count(t => t.Label.Length > 0));
        Assert.Contains(ticks, t => t.Label == "3 AM");
        Assert.Contains(ticks, t => t.Label == "12 PM");
    }

    [Fact]
    public void Hour_LabelsEveryInteriorHour() {
        var start = new DateTimeOffset(2026, 8, 25, 11, 0, 0, TimeSpan.Zero);

        var ticks = TimelineTicks.Generate(start, start.AddHours(6), TimelineZoom.Hour, Utc);

        Assert.Equal(5, ticks.Count);
        Assert.All(ticks, t => Assert.True(t.Label.Length > 0));
        Assert.Contains(ticks, t => t.Label == "12 PM");
    }

    [Fact]
    public void Month_LabelsOddDaysBetweenDailyGridLines() {
        var start = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

        var ticks = TimelineTicks.Generate(start, end, TimelineZoom.Month, Utc);

        Assert.Equal(30, ticks.Count(t => t.HasLine));
        Assert.Equal(16, ticks.Count(t => t.Label.Length > 0));
        Assert.Contains(ticks, t => t.Label == "8/25");
    }

    [Fact]
    public void Positions_AreStrictlyInsideTheWindow() {
        var start = new DateTimeOffset(2026, 8, 23, 0, 0, 0, TimeSpan.Zero);

        var ticks = TimelineTicks.Generate(start, start.AddDays(7), TimelineZoom.Week, Utc);

        Assert.All(ticks, t => Assert.InRange(t.LeftPercent, 0.001, 99.999));
    }
}
