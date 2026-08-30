using EggLedger.Web.Missions.Timeline;

namespace EggLedger.Web.Tests.Missions.Timeline;

public sealed class TimelineGridAnchorTests {
    private static readonly TimeZoneInfo Eastern = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
    private static readonly TimeZoneInfo Pacific = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
    private static readonly TimeZoneInfo India = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");

    [Fact]
    public void ExactlyAtEasternNoon_ReturnsItself() {
        var noon = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.FromHours(-4));

        var result = TimelineGridAnchor.GridDayStart(noon);

        Assert.Equal(noon, result);
    }

    [Fact]
    public void OneSecondBeforeEasternNoon_ReturnsPreviousDaysNoon() {
        var instant = new DateTimeOffset(2026, 6, 15, 11, 59, 59, TimeSpan.FromHours(-4));

        var result = TimelineGridAnchor.GridDayStart(instant);

        Assert.Equal(new DateTimeOffset(2026, 6, 14, 12, 0, 0, TimeSpan.FromHours(-4)), result);
    }

    [Fact]
    public void OneSecondAfterEasternNoon_ReturnsSameDaysNoon() {
        var instant = new DateTimeOffset(2026, 6, 15, 12, 0, 1, TimeSpan.FromHours(-4));

        var result = TimelineGridAnchor.GridDayStart(instant);

        Assert.Equal(new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.FromHours(-4)), result);
    }

    [Fact]
    public void SameMomentAcrossTimeZones_AgreeOnTheSameBoundaryInstant() {
        var moment = new DateTimeOffset(2026, 6, 15, 16, 5, 0, TimeSpan.Zero);
        var utcInstant = moment;
        var pacificInstant = TimeZoneInfo.ConvertTime(moment, Pacific);
        var indiaInstant = TimeZoneInfo.ConvertTime(moment, India);

        var fromUtc = TimelineGridAnchor.GridDayStart(utcInstant);
        var fromPacific = TimelineGridAnchor.GridDayStart(pacificInstant);
        var fromIndia = TimelineGridAnchor.GridDayStart(indiaInstant);

        var expected = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.FromHours(-4));
        Assert.Equal(expected, fromUtc);
        Assert.Equal(expected, fromPacific);
        Assert.Equal(expected, fromIndia);
    }

    [Theory]
    [InlineData(2026, 3, 8)]
    [InlineData(2026, 11, 1)]
    public void DstTransitionDate_GridDayIsExactly24HoursLong(int year, int month, int day) {
        var d = new DateTimeOffset(year, month, day, 20, 0, 0, TimeSpan.Zero);

        var thisDay = TimelineGridAnchor.GridDayStart(d);
        var nextDay = TimelineGridAnchor.GridDayStart(d.AddDays(1));

        Assert.Equal(TimeSpan.FromDays(1), nextDay - thisDay);
    }

    [Fact]
    public void GridWeekStart_LandsOnEasternSunday() {
        var center = new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

        var weekStart = TimelineGridAnchor.GridWeekStart(center);

        Assert.Equal(weekStart, TimelineGridAnchor.GridDayStart(weekStart));
        Assert.Equal(DayOfWeek.Sunday, TimeZoneInfo.ConvertTime(weekStart, Eastern).DayOfWeek);
        Assert.True(weekStart <= center);
        Assert.True(center - weekStart < TimeSpan.FromDays(7));
    }
}
