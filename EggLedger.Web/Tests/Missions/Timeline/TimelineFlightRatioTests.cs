using EggLedger.Domain.MissionPacking;
using EggLedger.Web.Missions.Timeline;

namespace EggLedger.Web.Tests.Missions.Timeline;

public sealed class TimelineFlightRatioTests {
    private static readonly DateTimeOffset WindowStart = new(2024, 3, 15, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset WindowEnd = new(2024, 3, 16, 0, 0, 0, TimeSpan.Zero);

    private static DatabaseMission M(string id, DateTimeOffset launch, DateTimeOffset ret, int missionType = 0) =>
        new() {
            MissiondId = id,
            LaunchDT = launch.ToUnixTimeSeconds(),
            ReturnDT = ret.ToUnixTimeSeconds(),
            MissionType = missionType,
        };

    [Fact]
    public void Compute_FullyFutureWindow_ReturnsNull() {
        var ratio = TimelineFlightRatio.Compute(
            [M("a", WindowStart, WindowEnd)], WindowStart, WindowEnd, WindowStart);

        Assert.Null(ratio);
    }

    [Fact]
    public void Compute_ThreeShipsFlyingTheWholeWindow_IsFullRatio() {
        var missions = new[] {
            M("a", WindowStart, WindowEnd),
            M("b", WindowStart, WindowEnd),
            M("c", WindowStart, WindowEnd),
        };

        var ratio = TimelineFlightRatio.Compute(missions, WindowStart, WindowEnd, WindowEnd);

        Assert.NotNull(ratio);
        Assert.Equal(1.0, ratio.Value, precision: 6);
    }

    [Fact]
    public void Compute_OneShipHalfTheWindow_IsOneSixth() {
        var missions = new[] { M("a", WindowStart, WindowStart.AddHours(12)) };

        var ratio = TimelineFlightRatio.Compute(missions, WindowStart, WindowEnd, WindowEnd);

        Assert.NotNull(ratio);
        Assert.Equal(1.0 / 6, ratio.Value, precision: 6);
    }

    [Fact]
    public void Compute_MissionOutsideWindow_ContributesNothing() {
        var missions = new[] { M("a", WindowStart.AddDays(-2), WindowStart.AddDays(-1)) };

        var ratio = TimelineFlightRatio.Compute(missions, WindowStart, WindowEnd, WindowEnd);

        Assert.NotNull(ratio);
        Assert.Equal(0, ratio.Value, precision: 6);
    }

    [Fact]
    public void Compute_PartiallyElapsedWindow_UsesNowAsTheDenominatorEnd() {
        var now = WindowStart.AddHours(12);
        var missions = new[] { M("a", WindowStart, WindowEnd) };

        var ratio = TimelineFlightRatio.Compute(missions, WindowStart, WindowEnd, now);

        Assert.NotNull(ratio);
        Assert.Equal(1.0 / 3, ratio.Value, precision: 6);
    }

    [Fact]
    public void Compute_ClipsMissionSpanToTheWindow() {
        var missions = new[] { M("a", WindowStart.AddHours(-12), WindowStart.AddHours(12)) };

        var ratio = TimelineFlightRatio.Compute(missions, WindowStart, WindowEnd, WindowEnd);

        Assert.NotNull(ratio);
        Assert.Equal(1.0 / 6, ratio.Value, precision: 6);
    }

    [Fact]
    public void ComputeByFarm_BothFarmsFlying_ReturnsHomeThenVirtueSlices() {
        var missions = new[] {
            M("h1", WindowStart, WindowEnd),
            M("h2", WindowStart, WindowEnd),
            M("h3", WindowStart, WindowEnd),
            M("v1", WindowStart, WindowEnd, missionType: 1),
        };

        var slices = TimelineFlightRatio.ComputeByFarm(missions, WindowStart, WindowEnd, WindowEnd);

        Assert.NotNull(slices);
        Assert.Equal(2, slices.Count);
        Assert.False(slices[0].IsVirtue);
        Assert.Equal(1.0, slices[0].Ratio, precision: 6);
        Assert.True(slices[1].IsVirtue);
        Assert.Equal(1.0 / 3, slices[1].Ratio, precision: 6);
    }

    [Fact]
    public void ComputeByFarm_OnlyVirtueFlying_ReturnsSingleVirtueSlice() {
        var missions = new[] { M("v1", WindowStart, WindowEnd, missionType: 1) };

        var slices = TimelineFlightRatio.ComputeByFarm(missions, WindowStart, WindowEnd, WindowEnd);

        Assert.NotNull(slices);
        var slice = Assert.Single(slices);
        Assert.True(slice.IsVirtue);
        Assert.Equal(1.0 / 3, slice.Ratio, precision: 6);
    }

    [Fact]
    public void ComputeByFarm_LegacyUnknownType_CountsAsHome() {
        var missions = new[] { M("a", WindowStart, WindowEnd, missionType: -1) };

        var slices = TimelineFlightRatio.ComputeByFarm(missions, WindowStart, WindowEnd, WindowEnd);

        Assert.NotNull(slices);
        var slice = Assert.Single(slices);
        Assert.False(slice.IsVirtue);
        Assert.Equal(1.0 / 3, slice.Ratio, precision: 6);
    }

    [Fact]
    public void ComputeByFarm_NoFlightTime_ReturnsSingleZeroHomeSlice() {
        var slices = TimelineFlightRatio.ComputeByFarm([], WindowStart, WindowEnd, WindowEnd);

        Assert.NotNull(slices);
        var slice = Assert.Single(slices);
        Assert.False(slice.IsVirtue);
        Assert.Equal(0, slice.Ratio, precision: 6);
    }

    [Fact]
    public void ComputeByFarm_FullyFutureWindow_ReturnsNull() {
        var slices = TimelineFlightRatio.ComputeByFarm(
            [M("a", WindowStart, WindowEnd)], WindowStart, WindowEnd, WindowStart);

        Assert.Null(slices);
    }

    [Fact]
    public void Compute_BothFarmsFullWindow_SumsAcrossFarms() {
        var missions = new[] {
            M("h1", WindowStart, WindowEnd),
            M("h2", WindowStart, WindowEnd),
            M("h3", WindowStart, WindowEnd),
            M("v1", WindowStart, WindowEnd, missionType: 1),
            M("v2", WindowStart, WindowEnd, missionType: 1),
            M("v3", WindowStart, WindowEnd, missionType: 1),
        };

        var ratio = TimelineFlightRatio.Compute(missions, WindowStart, WindowEnd, WindowEnd);

        Assert.NotNull(ratio);
        Assert.Equal(2.0, ratio.Value, precision: 6);
    }
}
