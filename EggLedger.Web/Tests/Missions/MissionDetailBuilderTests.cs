using EggLedger.Domain.MissionPacking;
using EggLedger.Domain.MissionQuery;
using EggLedger.Web.Missions;
using Ei;

namespace EggLedger.Web.Tests.Missions;

public sealed class MissionDetailBuilderTests {
    private static MissionDrop Drop(string spec, int id = 1, int level = 0, int rarity = 0) =>
        new() { SpecType = spec, Id = id, Level = level, Rarity = rarity };

    private static DatabaseMission M(
        string id, int capacity = 4, int nominal = 4,
        MissionInfo.Spaceship ship = MissionInfo.Spaceship.ChickenOne,
        MissionInfo.DurationType dur = MissionInfo.DurationType.Short,
        int level = 0, int target = -1) => new() {
            MissiondId = id,
            Capacity = capacity,
            NominalCapcity = nominal,
            Ship = ship,
            DurationType = dur,
            Level = level,
            TargetInt = target,
            DurationString = "Short",
        };

    [Fact]
    public void BuildBase_SplitsDropsBySpecType() {
        var drops = new[] {
            Drop("Artifact"),
            Drop("Stone"),
            Drop("StoneFragment"),
            Drop("Ingredient"),
            Drop("Artifact"),
        };
        var data = MissionDetailBuilder.BuildBase(M("m1"), drops, Array.Empty<DatabaseMission>(), extendedInfo: false, TimeZoneInfo.Utc);
        Assert.Single(data.Artifacts);
        Assert.Equal(2, data.Artifacts[0].Count);
        Assert.Single(data.Stones);
        Assert.Single(data.StoneFragments);
        Assert.Single(data.Ingredients);
    }

    [Fact]
    public void BuildBase_CapacityModifierClampedToTwo() {
        var huge = M("m1", capacity: 100, nominal: 4);
        var d = MissionDetailBuilder.BuildBase(huge, Array.Empty<MissionDrop>(), Array.Empty<DatabaseMission>(), false, TimeZoneInfo.Utc);
        Assert.Equal(2, d.CapacityModifier);

        var normal = M("m1", capacity: 6, nominal: 4);
        var d2 = MissionDetailBuilder.BuildBase(normal, Array.Empty<MissionDrop>(), Array.Empty<DatabaseMission>(), false, TimeZoneInfo.Utc);
        Assert.Equal(1.5, d2.CapacityModifier);
    }

    [Fact]
    public void BuildBase_NominalZeroTreatedAsOne() {
        var m = M("m1", capacity: 1, nominal: 0);
        var d = MissionDetailBuilder.BuildBase(m, Array.Empty<MissionDrop>(), Array.Empty<DatabaseMission>(), false, TimeZoneInfo.Utc);
        Assert.Equal(1, d.CapacityModifier);
    }

    [Fact]
    public void BuildBase_PrevNextFromFilteredList() {
        var list = new[] { M("a"), M("b"), M("c") };
        var d = MissionDetailBuilder.BuildBase(list[1], Array.Empty<MissionDrop>(), list, extendedInfo: true, TimeZoneInfo.Utc);
        Assert.Equal("a", d.PrevMission);
        Assert.Equal("c", d.NextMission);
    }

    [Fact]
    public void BuildBase_PrevNull_NextNull_AtEdges() {
        var list = new[] { M("a"), M("b") };
        var first = MissionDetailBuilder.BuildBase(list[0], Array.Empty<MissionDrop>(), list, true, TimeZoneInfo.Utc);
        Assert.Null(first.PrevMission);
        Assert.Equal("b", first.NextMission);

        var last = MissionDetailBuilder.BuildBase(list[1], Array.Empty<MissionDrop>(), list, true, TimeZoneInfo.Utc);
        Assert.Equal("a", last.PrevMission);
        Assert.Null(last.NextMission);
    }

    [Fact]
    public void BuildBase_NoExtendedInfo_NoPrevNext() {
        var list = new[] { M("a"), M("b") };
        var d = MissionDetailBuilder.BuildBase(list[1], Array.Empty<MissionDrop>(), list, extendedInfo: false, TimeZoneInfo.Utc);
        Assert.Null(d.PrevMission);
        Assert.Null(d.NextMission);
    }

    [Fact]
    public void MennoKey_RemapsMinusOneTarget() {
        var m = M("m1", ship: MissionInfo.Spaceship.ChickenHeavy, dur: MissionInfo.DurationType.Tutorial, level: 3, target: -1);
        Assert.Equal("2_3_3_10000", MissionDetailBuilder.MennoKey(m));
    }

    [Fact]
    public void MennoKey_KeepsRealTarget() {
        var m = M("m1", ship: MissionInfo.Spaceship.ChickenOne, dur: MissionInfo.DurationType.Short, level: 0, target: 42);
        Assert.Equal("0_0_0_42", MissionDetailBuilder.MennoKey(m));
    }

    [Fact]
    public void ApplySortMethod_ReSortsAllLists() {
        var drops = new[] {
            Drop("Artifact", id: 1, level: 0, rarity: 0),
            Drop("Artifact", id: 2, level: 1, rarity: 0),
        };
        var data = MissionDetailBuilder.BuildBase(M("m1"), drops, Array.Empty<DatabaseMission>(), false, TimeZoneInfo.Utc);
        MissionDetailBuilder.ApplySortMethod(data, MissionSortMethod.Default);

        Assert.Equal(0, data.Artifacts[0].Level);
        Assert.Equal(1, data.Artifacts[1].Level);
    }
}
