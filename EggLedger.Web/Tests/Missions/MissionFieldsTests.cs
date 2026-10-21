using Ei;
using EggIdentity.Canvas;
using EggLedger.Domain.MissionPacking;
using EggLedger.Domain.MissionQuery;
using EggLedger.Web.Missions;
using EggLedger.Web.Missions.Model;

namespace EggLedger.Web.Tests.Missions;

public class MissionFieldsTests {
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    private static DatabaseMission BuildMission(bool dubCap = false, bool buggedCap = false) => new() {
        LaunchDT = 1700000000,
        ReturnDT = 1700003600,
        ShipString = "Henerprise",
        DurationType = MissionInfo.DurationType.Long,
        DurationString = "1h",
        Level = 4,
        Capacity = 1000,
        IsDubCap = dubCap,
        IsBuggedCap = buggedCap,
        Target = "space_station",
    };

    private static MissionCardData BuildData(DatabaseMission mission, int? dropCount = null, MissionDropSummary? drops = null, double? expected = null) =>
        new(mission, Utc, dropCount, drops, expected);

    [Fact]
    public void Resolve_Ship_ReturnsShipString() {
        var data = BuildData(BuildMission());
        Assert.Equal("Henerprise", MissionFields.Resolve(MissionFields.Ship, data));
    }

    [Fact]
    public void Resolve_Level_ReturnsLevel() {
        var data = BuildData(BuildMission());
        Assert.Equal(4, MissionFields.Resolve(MissionFields.Level, data));
    }

    [Fact]
    public void Resolve_Launch_ReturnsLedgerDate() {
        var mission = BuildMission();
        var data = BuildData(mission);
        var expected = MissionFilterMatcher.LedgerDate(mission.LaunchDT, Utc);
        Assert.Equal(expected, MissionFields.Resolve(MissionFields.Launch, data));
    }

    [Fact]
    public void Resolve_LaunchDate_ReturnsLedgerDate() {
        var mission = BuildMission();
        var data = BuildData(mission);
        var expected = MissionFilterMatcher.LedgerDate(mission.LaunchDT, Utc);
        Assert.Equal(expected, MissionFields.Resolve(MissionFields.LaunchDate, data));
    }

    [Fact]
    public void Resolve_LaunchTime_ReturnsLedgerDate() {
        var mission = BuildMission();
        var data = BuildData(mission);
        var expected = MissionFilterMatcher.LedgerDate(mission.LaunchDT, Utc);
        Assert.Equal(expected, MissionFields.Resolve(MissionFields.LaunchTime, data));
    }

    [Fact]
    public void Resolve_Return_ReturnsLedgerDate() {
        var mission = BuildMission();
        var data = BuildData(mission);
        var expected = MissionFilterMatcher.LedgerDate(mission.ReturnDT, Utc);
        Assert.Equal(expected, MissionFields.Resolve(MissionFields.Return, data));
    }

    [Fact]
    public void Resolve_Duration_ReturnsDurationString() {
        var data = BuildData(BuildMission());
        Assert.Equal("1h", MissionFields.Resolve(MissionFields.Duration, data));
    }

    [Fact]
    public void Resolve_DurationType_ReturnsDurationType() {
        var data = BuildData(BuildMission());
        Assert.Equal(MissionInfo.DurationType.Long, MissionFields.Resolve(MissionFields.DurationType, data));
    }

    [Fact]
    public void Resolve_Target_ReturnsDisplayName() {
        var data = BuildData(BuildMission());
        Assert.Equal("Space Station", MissionFields.Resolve(MissionFields.Target, data));
    }

    [Fact]
    public void Resolve_Capacity_ReturnsCapacity() {
        var data = BuildData(BuildMission());
        Assert.Equal(1000, MissionFields.Resolve(MissionFields.Capacity, data));
    }

    [Fact]
    public void Resolve_CapModifier_DubCap_ReturnsTwoX() {
        var data = BuildData(BuildMission(dubCap: true));
        Assert.Equal("2x", MissionFields.Resolve(MissionFields.CapModifier, data));
    }

    [Fact]
    public void Resolve_CapModifier_BuggedCap_ReturnsPointSixX() {
        var data = BuildData(BuildMission(buggedCap: true));
        Assert.Equal("0.6x", MissionFields.Resolve(MissionFields.CapModifier, data));
    }

    [Fact]
    public void Resolve_CapModifier_Neither_ReturnsNull() {
        var data = BuildData(BuildMission());
        Assert.Null(MissionFields.Resolve(MissionFields.CapModifier, data));
    }

    [Fact]
    public void Resolve_DropCount_ReturnsDropCount() {
        var data = BuildData(BuildMission(), dropCount: 5);
        Assert.Equal(5, MissionFields.Resolve(MissionFields.DropCount, data));
    }

    [Fact]
    public void Resolve_Artifacts_ReturnsSummaryArtifacts() {
        var drops = MissionDropSummary.From([MakeDrop("Artifact", 1, 0)], 6);
        var data = BuildData(BuildMission(), drops: drops);
        Assert.Equal(1, MissionFields.Resolve(MissionFields.Artifacts, data));
    }

    [Fact]
    public void Resolve_UnknownId_ReturnsNull() {
        var data = BuildData(BuildMission());
        Assert.Null(MissionFields.Resolve("not_a_field", data));
    }

    [Fact]
    public void Format_Launch_UsesDateTimeFormat() {
        var mission = BuildMission();
        var data = BuildData(mission);
        var expected = MissionFilterMatcher.LedgerDate(mission.LaunchDT, Utc).ToString("yyyy-MM-dd HH:mm:ss");
        Assert.Equal(expected, MissionFields.Format(MissionFields.Launch, data));
    }

    [Fact]
    public void Format_LaunchDate_UsesDateFormat() {
        var mission = BuildMission();
        var data = BuildData(mission);
        var expected = MissionFilterMatcher.LedgerDate(mission.LaunchDT, Utc).ToString("yyyy-MM-dd");
        Assert.Equal(expected, MissionFields.Format(MissionFields.LaunchDate, data));
    }

    [Fact]
    public void Format_LaunchTime_UsesTimeFormat() {
        var mission = BuildMission();
        var data = BuildData(mission);
        var expected = MissionFilterMatcher.LedgerDate(mission.LaunchDT, Utc).ToString("HH:mm:ss");
        Assert.Equal(expected, MissionFields.Format(MissionFields.LaunchTime, data));
    }

    [Fact]
    public void Format_Duration_ReturnsDurationString() {
        var data = BuildData(BuildMission());
        Assert.Equal("1h", MissionFields.Format(MissionFields.Duration, data));
    }

    [Fact]
    public void Format_CapModifier_NullWhenNeither() {
        var data = BuildData(BuildMission());
        Assert.Equal("-", MissionFields.Format(MissionFields.CapModifier, data));
    }

    [Fact]
    public void Format_CapModifier_DubCap() {
        var data = BuildData(BuildMission(dubCap: true));
        Assert.Equal("2x", MissionFields.Format(MissionFields.CapModifier, data));
    }

    [Fact]
    public void NeedsDrops_StringKind_TrueForDropStripRarityBarDropTypes() {
        Assert.True(CardKinds.NeedsDrops(CardKinds.DropStrip));
        Assert.True(CardKinds.NeedsDrops(CardKinds.RarityBar));
        Assert.True(CardKinds.NeedsDrops(CardKinds.DropTypeBars));
    }

    [Fact]
    public void NeedsDrops_StringKind_FalseForOtherKinds() {
        Assert.False(CardKinds.NeedsDrops(CardKinds.Field));
        Assert.False(CardKinds.NeedsDrops(CardKinds.ShipImage));
        Assert.False(CardKinds.NeedsDrops(CardKinds.Expected));
    }

    [Fact]
    public void NeedsDrops_Doc_TrueWhenAnyItemNeedsDrops() {
        var doc = new CanvasDoc(24, 2, [new CanvasItem("a", CardKinds.Field, 0, 0, 4, 1, 0), new CanvasItem("b", CardKinds.RarityBar, 4, 0, 4, 1, 0)]);
        Assert.True(CardKinds.NeedsDrops(doc));
    }

    [Fact]
    public void NeedsDrops_Doc_FalseWhenNoItemNeedsDrops() {
        var doc = new CanvasDoc(24, 2, [new CanvasItem("a", CardKinds.Field, 0, 0, 4, 1, 0), new CanvasItem("b", CardKinds.ShipImage, 4, 0, 4, 1, 0)]);
        Assert.False(CardKinds.NeedsDrops(doc));
    }

    [Fact]
    public void CardPayload_Field_ParsesFieldId() {
        var item = new CanvasItem("a", CardKinds.Field, 0, 0, 4, 1, 0) { Payload = "{\"field\":\"ship\"}" };
        Assert.Equal("ship", CardPayload.Field(item));
    }

    [Fact]
    public void CardPayload_Field_NullWhenNoPayload() {
        var item = new CanvasItem("a", CardKinds.Field, 0, 0, 4, 1, 0);
        Assert.Null(CardPayload.Field(item));
    }

    [Fact]
    public void CardPayload_Top_ParsesTopCount() {
        var item = new CanvasItem("a", CardKinds.DropStrip, 0, 0, 4, 1, 0) { Payload = "{\"top\":6}" };
        Assert.Equal(6, CardPayload.Top(item));
    }

    [Fact]
    public void CardPayload_Vertical_ParsesBoolean() {
        var item = new CanvasItem("a", CardKinds.Divider, 0, 0, 1, 1, 0) { Payload = "{\"vertical\":true}" };
        Assert.True(CardPayload.Vertical(item));
    }

    [Fact]
    public void MissionDropSummary_From_CountsBySpecType() {
        var drops = new List<MissionDrop> {
            MakeDrop("Artifact", 1, 0),
            MakeDrop("Artifact", 2, 1),
            MakeDrop("Stone", 0, 2),
            MakeDrop("Ingredient", 0, 3),
            MakeDrop("StoneFragment", 0, 4),
        };

        var summary = MissionDropSummary.From(drops, 10);

        Assert.Equal(2, summary.Artifacts);
        Assert.Equal(1, summary.Stones);
        Assert.Equal(1, summary.Ingredients);
        Assert.Equal(1, summary.Fragments);
    }

    [Fact]
    public void MissionDropSummary_From_RarityCountsIndexedByRarity() {
        var drops = new List<MissionDrop> {
            MakeDrop("Artifact", 0, 0),
            MakeDrop("Artifact", 0, 1),
            MakeDrop("Artifact", 2, 2),
            MakeDrop("Artifact", 3, 3),
        };

        var summary = MissionDropSummary.From(drops, 10);

        Assert.Equal(4, summary.RarityCounts.Count);
        Assert.Equal(2, summary.RarityCounts[0]);
        Assert.Equal(0, summary.RarityCounts[1]);
        Assert.Equal(1, summary.RarityCounts[2]);
        Assert.Equal(1, summary.RarityCounts[3]);
    }

    [Fact]
    public void MissionDropSummary_From_TopOrdersByRarityDescThenIVOrderAsc() {
        var low = MakeDrop("Artifact", 0, 5);
        var highFirst = MakeDrop("Artifact", 3, 2);
        var highSecond = MakeDrop("Artifact", 3, 1);
        var mid = MakeDrop("Artifact", 1, 0);
        var drops = new List<MissionDrop> { low, highFirst, mid, highSecond };

        var summary = MissionDropSummary.From(drops, 10);

        Assert.Equal([highSecond, highFirst, mid, low], summary.Top);
    }

    [Fact]
    public void MissionDropSummary_From_TopRespectsTakeLimit() {
        var drops = Enumerable.Range(0, 10).Select(i => MakeDrop("Artifact", 0, i)).ToList();
        var summary = MissionDropSummary.From(drops, 3);
        Assert.Equal(3, summary.Top.Count);
    }

    [Fact]
    public void Catalog_ContainsEveryConstantId() {
        var ids = new[] {
            MissionFields.Ship, MissionFields.Level, MissionFields.Launch, MissionFields.LaunchDate, MissionFields.LaunchTime,
            MissionFields.Return, MissionFields.Duration, MissionFields.DurationType, MissionFields.Target, MissionFields.Capacity,
            MissionFields.CapModifier, MissionFields.DropCount, MissionFields.Artifacts, MissionFields.Stones,
            MissionFields.Ingredients, MissionFields.Fragments,
        };

        foreach (var id in ids) {
            Assert.NotNull(MissionFields.Catalog.Find(id));
        }
    }

    private static MissionDrop MakeDrop(string specType, int rarity, int ivOrder) => new() {
        SpecType = specType,
        Rarity = rarity,
        IVOrder = ivOrder,
    };
}
