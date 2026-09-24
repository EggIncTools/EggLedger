using EggLedger.Domain.MissionPacking;
using EggLedger.Web.Missions;

namespace EggLedger.Web.Tests.Missions;

public sealed class MissionViewOptionsTests {
    private static DatabaseMission M(int type, string id) => new() { MissionType = type, MissiondId = id };

    [Fact]
    public void Defaults_MatchVue() {
        var o = new MissionViewOptions();
        Assert.False(o.ViewByDate);
        Assert.True(o.ViewMissionTimes);
        Assert.Equal(MultiViewMode.Off, o.MultiViewMode);
        Assert.Equal(MissionSortMethod.Default, o.SortMethod);
        Assert.False(o.SortByDropCount);
        Assert.Null(o.MissionTypeTab);
    }

    [Fact]
    public void HasBothMissionTypes_NeedsBothHomeAndVirtue() {
        Assert.False(MissionViewOptions.HasBothMissionTypes(null));
        Assert.False(MissionViewOptions.HasBothMissionTypes([]));
        Assert.False(MissionViewOptions.HasBothMissionTypes([M(0, "a"), M(0, "b")]));
        Assert.False(MissionViewOptions.HasBothMissionTypes([M(1, "a")]));
        Assert.True(MissionViewOptions.HasBothMissionTypes([M(0, "a"), M(1, "b")]));
    }

    [Fact]
    public void TabFilteredMissions_NullTabPassesThrough() {
        var missions = new[] { M(0, "a"), M(1, "b") };
        Assert.Same(missions, MissionViewOptions.TabFilteredMissions(missions, null));
    }

    [Fact]
    public void TabFilteredMissions_FiltersByType() {
        var missions = new[] { M(0, "a"), M(1, "b"), M(0, "c") };
        var home = MissionViewOptions.TabFilteredMissions(missions, 0)!;
        Assert.Equal(["a", "c"], [.. home.Select(m => m.MissiondId)]);
        var virtue = MissionViewOptions.TabFilteredMissions(missions, 1)!;
        Assert.Single(virtue);
        Assert.Equal("b", virtue[0].MissiondId);
    }

    [Fact]
    public void TabFilteredMissions_NullInputReturnsNull() =>
        Assert.Null(MissionViewOptions.TabFilteredMissions(null, 0));

    [Theory]
    [InlineData("row", MultiViewMode.Row)]
    [InlineData("free", MultiViewMode.Free)]
    [InlineData("off", MultiViewMode.Off)]
    [InlineData("garbage", MultiViewMode.Off)]
    [InlineData(null, MultiViewMode.Off)]
    public void ParseMultiViewMode(string? raw, MultiViewMode expected) =>
        Assert.Equal(expected, MissionViewOptions.ParseMultiViewMode(raw));

    [Theory]
    [InlineData(MultiViewMode.Row, "row")]
    [InlineData(MultiViewMode.Free, "free")]
    [InlineData(MultiViewMode.Off, "off")]
    public void MultiViewModeToString(MultiViewMode mode, string expected) =>
        Assert.Equal(expected, MissionViewOptions.MultiViewModeToString(mode));

    [Theory]
    [InlineData("iv", MissionSortMethod.Iv)]
    [InlineData("default", MissionSortMethod.Default)]
    [InlineData(null, MissionSortMethod.Default)]
    public void ParseSortMethod(string? raw, MissionSortMethod expected) =>
        Assert.Equal(expected, MissionViewOptions.ParseSortMethod(raw));

    [Fact]
    public void LoadFrom_HydratesFromSettingsMap() {
        var o = new MissionViewOptions();
        o.LoadFrom(new Dictionary<string, string> {
            [MissionViewOptions.KeyViewByDate] = "true",
            [MissionViewOptions.KeyViewTimes] = "false",
            [MissionViewOptions.KeyMultiViewMode] = "free",
            [MissionViewOptions.KeySortMethod] = "iv",
            [MissionViewOptions.KeySortByDropCount] = "true",
        });
        Assert.True(o.ViewByDate);
        Assert.False(o.ViewMissionTimes);
        Assert.Equal(MultiViewMode.Free, o.MultiViewMode);
        Assert.Equal(MissionSortMethod.Iv, o.SortMethod);
        Assert.True(o.SortByDropCount);
    }

    [Fact]
    public void LoadFrom_MissingKeysKeepDefaults() {
        var o = new MissionViewOptions();
        o.LoadFrom(new Dictionary<string, string>());
        Assert.False(o.ViewByDate);
        Assert.True(o.ViewMissionTimes);
    }
}
