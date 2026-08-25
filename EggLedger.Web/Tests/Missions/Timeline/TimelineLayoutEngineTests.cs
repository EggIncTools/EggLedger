using EggLedger.Domain.MissionPacking;
using EggLedger.Web.Missions.Timeline;

namespace EggLedger.Web.Tests.Missions.Timeline;

public sealed class TimelineLayoutEngineTests {
    private static readonly DateTimeOffset WindowStart = new(2024, 3, 15, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset WindowEnd = new(2024, 3, 16, 0, 0, 0, TimeSpan.Zero);

    private static DatabaseMission M(string id, DateTimeOffset launch, DateTimeOffset ret, string ship = "CHICKEN_ONE") =>
        new() {
            MissiondId = id,
            LaunchDT = launch.ToUnixTimeSeconds(),
            ReturnDT = ret.ToUnixTimeSeconds(),
            ShipEnumString = ship,
            ShipString = "Chicken One",
        };

    [Fact]
    public void Layout_ExcludesMissionsOutsideTheWindow() {
        var missions = new[] {
            M("before", WindowStart.AddDays(-2), WindowStart.AddDays(-1)),
            M("inside", WindowStart.AddHours(1), WindowStart.AddHours(2)),
            M("after", WindowEnd.AddDays(1), WindowEnd.AddDays(2)),
        };

        var bars = TimelineLayoutEngine.Layout(missions, WindowStart, WindowEnd, WindowStart);

        Assert.Single(bars);
        Assert.Equal("inside", bars[0].MissionId);
    }

    [Fact]
    public void Layout_IncludesMissionsThatOnlyPartiallyOverlapTheWindow() {
        var missions = new[] {
            M("straddlesStart", WindowStart.AddHours(-2), WindowStart.AddHours(2)),
            M("straddlesEnd", WindowEnd.AddHours(-2), WindowEnd.AddHours(2)),
        };

        var bars = TimelineLayoutEngine.Layout(missions, WindowStart, WindowEnd, WindowStart);

        Assert.Equal(2, bars.Count);
    }

    [Fact]
    public void Layout_ClipsBarPositionToTheWindow() {
        var missions = new[] { M("straddles", WindowStart.AddHours(-6), WindowStart.AddHours(6)) };

        var bars = TimelineLayoutEngine.Layout(missions, WindowStart, WindowEnd, WindowStart);

        Assert.Equal(0, bars[0].LeftPercent, precision: 3);
        Assert.Equal(25, bars[0].WidthPercent, precision: 3);
    }

    [Fact]
    public void Layout_NonOverlappingMissionsShareOneLane() {
        var missions = new[] {
            M("first", WindowStart.AddHours(1), WindowStart.AddHours(2)),
            M("second", WindowStart.AddHours(3), WindowStart.AddHours(4)),
        };

        var bars = TimelineLayoutEngine.Layout(missions, WindowStart, WindowEnd, WindowStart);

        Assert.All(bars, b => Assert.Equal(0, b.Lane));
    }

    [Fact]
    public void Layout_OverlappingMissionsGetSeparateLanes() {
        var missions = new[] {
            M("a", WindowStart.AddHours(1), WindowStart.AddHours(5)),
            M("b", WindowStart.AddHours(2), WindowStart.AddHours(3)),
            M("c", WindowStart.AddHours(2), WindowStart.AddHours(3)),
        };

        var bars = TimelineLayoutEngine.Layout(missions, WindowStart, WindowEnd, WindowStart);

        var lanes = bars.Select(b => b.Lane).OrderBy(l => l).ToList();
        Assert.Equal([0, 1, 2], lanes);
    }

    [Fact]
    public void Layout_LaneIsReusedOnceItsOccupantHasEnded() {
        var missions = new[] {
            M("a", WindowStart.AddHours(1), WindowStart.AddHours(2)),
            M("b", WindowStart.AddHours(1), WindowStart.AddHours(2)),
            M("c", WindowStart.AddHours(3), WindowStart.AddHours(4)),
        };

        var bars = TimelineLayoutEngine.Layout(missions, WindowStart, WindowEnd, WindowStart);

        var byId = bars.ToDictionary(b => b.MissionId);
        Assert.Equal(2, bars.Select(b => b.Lane).Distinct().Count());
        Assert.True(byId["c"].Lane == byId["a"].Lane || byId["c"].Lane == byId["b"].Lane);
    }

    [Fact]
    public void Layout_CompletedMissionIsFullyFilledAndNotActive() {
        var missions = new[] { M("done", WindowStart.AddHours(1), WindowStart.AddHours(3)) };

        var bars = TimelineLayoutEngine.Layout(missions, WindowStart, WindowEnd, WindowStart.AddHours(5));

        Assert.False(bars[0].IsActive);
        Assert.Equal(100, bars[0].FillPercent, precision: 3);
    }

    [Fact]
    public void Layout_InFlightMissionFillPercentReflectsElapsedFraction() {
        var missions = new[] { M("flying", WindowStart.AddHours(0), WindowStart.AddHours(4)) };

        var bars = TimelineLayoutEngine.Layout(missions, WindowStart, WindowEnd, WindowStart.AddHours(1));

        Assert.True(bars[0].IsActive);
        Assert.Equal(25, bars[0].FillPercent, precision: 3);
    }

    [Fact]
    public void Layout_ClippedInFlightMissionPutsFillBoundaryAtNow() {
        var missions = new[] { M("clipped", WindowStart.AddHours(-24), WindowStart.AddHours(12)) };

        var bars = TimelineLayoutEngine.Layout(missions, WindowStart, WindowEnd, WindowStart.AddHours(6));

        Assert.True(bars[0].IsActive);
        Assert.Equal(50, bars[0].FillPercent, precision: 3);
    }

    [Fact]
    public void Layout_MissionTouchingWindowEdgeWithZeroVisibleSpanIsExcluded() {
        var missions = new[] {
            M("endsAtStart", WindowStart.AddHours(-2), WindowStart),
            M("startsAtEnd", WindowEnd, WindowEnd.AddHours(2)),
        };

        var bars = TimelineLayoutEngine.Layout(missions, WindowStart, WindowEnd, WindowStart);

        Assert.Empty(bars);
    }

    [Fact]
    public void Layout_ShipIconPathAndNameComeFromTheMission() {
        var missions = new[] { M("ship", WindowStart.AddHours(1), WindowStart.AddHours(2), ship: "ATREGGIES") };

        var bars = TimelineLayoutEngine.Layout(missions, WindowStart, WindowEnd, WindowStart);

        Assert.Contains("ATREGGIES.png", bars[0].ShipIconPath);
        Assert.Equal("Chicken One", bars[0].ShipName);
    }

    [Fact]
    public void Layout_EmptyInput_ReturnsEmpty() {
        var bars = TimelineLayoutEngine.Layout(Array.Empty<DatabaseMission>(), WindowStart, WindowEnd, WindowStart);

        Assert.Empty(bars);
    }
}
