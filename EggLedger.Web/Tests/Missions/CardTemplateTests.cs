using EggIdentity.Canvas;
using EggLedger.Web.Missions.Model;

namespace EggLedger.Web.Tests.Missions;

public class CardTemplateTests {
    private static readonly string[] BuiltinNames = ["Classic", "CalendarDay", "CalendarWeek", "CalendarMonth", "Agenda"];

    private static CardTemplate Custom(string name = "Mine") => new(
        name,
        new CanvasDoc(10, 2, [
            new CanvasItem("bg", CardKinds.Box, 1, 1, 10, 2, 0) { Style = new ItemStyle { Background = "#112233", Border = "#445566", Radius = 2 } },
            new CanvasItem("ship", CardKinds.Field, 1, 1, 4, 1, 1) { Payload = "{\"field\":\"ship\"}" },
            new CanvasItem("label", CardKinds.Label, 5, 2, 3, 1, 2) { Payload = "{\"text\":\"hi\"}", Style = new ItemStyle { FontWeight = 700, Align = TextAlign.Center } },
        ]),
        ".mc { color: red; }",
        false);

    private static MissionCardConfig Legacy(string name, string? background, string? border, string? accent, (CardField Field, bool Enabled, int Order)[] fields) =>
        new(name, CardLayout.CompactRow, [.. fields.Select(f => new CardFieldSlot(f.Field, f.Enabled, f.Order))], true, false, background, border, accent, null);

    private static MissionCardConfig Legacy(string name, (CardField Field, bool Enabled, int Order)[] fields) => Legacy(name, null, null, null, fields);

    private static CanvasItem Item(CardTemplate template, string id) => template.Doc.Items.Single(i => i.Id == id);

    [Fact]
    public void Default_HasEveryBuiltinAndEachSlotPointsAtItsBuiltin() {
        var set = CardTemplateSet.Default;

        Assert.Equal(BuiltinNames, set.Templates.Select(t => t.Name));
        Assert.Equal("Classic", set.Slots[CardSlot.List]);
        Assert.Equal("CalendarDay", set.Slots[CardSlot.CalendarDay]);
        Assert.Equal("CalendarWeek", set.Slots[CardSlot.CalendarWeek]);
        Assert.Equal("CalendarMonth", set.Slots[CardSlot.CalendarMonth]);
        Assert.Equal("Agenda", set.Slots[CardSlot.Agenda]);
    }

    [Fact]
    public void Classic_LaysTheOldRowOutLeftToRight() {
        var doc = CardBuiltins.Classic.Doc;

        Assert.Equal((24, 2), (doc.Cols, doc.Rows));
        Assert.Equal(["launch_date", "launch_time", "ship_img", "ship", "stars", "target_img", "target", "drop_count"], doc.Items.Select(i => i.Id));
        Assert.Equal([1, 4, 7, 8, 13, 15, 16, 21], doc.Items.Select(i => i.X));
        Assert.All(doc.Items, i => Assert.Equal(1, i.Y));
        Assert.All(doc.Items, i => Assert.Null(i.Style));
        Assert.Equal(CardKinds.ShipImage, Item(CardBuiltins.Classic, "ship_img").Kind);
        Assert.Equal(MissionFields.LaunchDate, CardPayload.Field(Item(CardBuiltins.Classic, "launch_date")));
    }

    [Fact]
    public void Agenda_UsesThreeRows() {
        var doc = CardBuiltins.Agenda.Doc;

        Assert.Equal((12, 3), (doc.Cols, doc.Rows));
        Assert.Equal([1, 1, 1, 1, 2, 2, 2, 3, 3], doc.Items.Select(i => i.Y));
    }

    [Fact]
    public void CalendarMonth_HoldsOnlyImages() {
        var doc = CardBuiltins.CalendarMonth.Doc;

        Assert.Equal((4, 1), (doc.Cols, doc.Rows));
        Assert.Equal([CardKinds.ShipImage, CardKinds.TargetImage], doc.Items.Select(i => i.Kind));
    }

    [Fact]
    public void Codec_RoundTripsAModifiedSet() {
        var set = CardTemplateSet.Default
            .With(Custom())
            .With(CardBuiltins.Agenda with { CustomCss = ".x{}" })
            .Assign(CardSlot.List, "Mine")
            .Assign(CardSlot.CalendarWeek, "Mine");

        var back = CardTemplateCodec.Deserialize(CardTemplateCodec.Serialize(set));

        Assert.Equal(set, back);
        Assert.Equal(set.GetHashCode(), back.GetHashCode());
    }

    [Fact]
    public void Codec_RoundTripsDefault() {
        Assert.Equal(CardTemplateSet.Default, CardTemplateCodec.Deserialize(CardTemplateCodec.Serialize(CardTemplateSet.Default)));
    }

    [Fact]
    public void Codec_WritesTheWrapperShape() {
        var json = CardTemplateCodec.Serialize(CardTemplateSet.Default);

        Assert.Contains("\"version\":1", json);
        Assert.Contains("\"list\":\"Classic\"", json);
        Assert.Contains("\"calendarDay\":\"CalendarDay\"", json);
        Assert.Contains("\"doc\":{", json);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("[1,2,3]")]
    [InlineData("\"text\"")]
    [InlineData("{\"version\":2,\"templates\":[],\"slots\":{}}")]
    [InlineData("{\"version\":\"one\"}")]
    public void Codec_GarbageBlankOrNewerYieldsDefault(string? json) {
        Assert.Equal(CardTemplateSet.Default, CardTemplateCodec.Deserialize(json!));
    }

    [Fact]
    public void Codec_DropsTemplatesWhoseDocFails() {
        var json = "{\"version\":1,\"templates\":[{\"name\":\"Bad\",\"doc\":{\"version\":99,\"cols\":4,\"rows\":1,\"items\":[]}},{\"name\":\"Worse\",\"doc\":\"nope\"}],\"slots\":{\"list\":\"Bad\"}}";

        Assert.Equal(CardTemplateSet.Default, CardTemplateCodec.Deserialize(json));
    }

    [Fact]
    public void Codec_AlwaysAppendsMissingBuiltins() {
        var json = CardTemplateCodec.Serialize(new CardTemplateSet([Custom()], new Dictionary<CardSlot, string> { [CardSlot.List] = "Mine" }));

        var set = CardTemplateCodec.Deserialize(json);

        Assert.Equal(["Mine", .. BuiltinNames], set.Templates.Select(t => t.Name));
        Assert.Equal("Mine", set.Slots[CardSlot.List]);
        Assert.Equal("Agenda", set.Slots[CardSlot.Agenda]);
    }

    [Fact]
    public void Codec_UnknownSlotTemplateFallsBackToTheSlotBuiltin() {
        var json = "{\"version\":1,\"templates\":[],\"slots\":{\"list\":\"Ghost\",\"agenda\":\"Classic\"}}";

        var set = CardTemplateCodec.Deserialize(json);

        Assert.Equal("Classic", set.Slots[CardSlot.List]);
        Assert.Equal("Classic", set.Slots[CardSlot.Agenda]);
        Assert.Equal("CalendarDay", set.Slots[CardSlot.CalendarDay]);
    }

    [Fact]
    public void Codec_UserEditedClassicReplacesTheBuiltin() {
        var edited = CardBuiltins.Classic with { Doc = CardBuiltins.Classic.Doc with { Rows = 3 }, ColorizeByDuration = false };
        var json = CardTemplateCodec.Serialize(CardTemplateSet.Default.With(edited));

        var set = CardTemplateCodec.Deserialize(json);

        Assert.Single(set.Templates, t => t.Name == "Classic");
        Assert.Equal(edited, set.For(CardSlot.List));
        Assert.Equal(BuiltinNames, set.Templates.Select(t => t.Name));
    }

    [Fact]
    public void For_FallsBackToTheSlotBuiltinWhenTheNameIsUnknown() {
        var set = new CardTemplateSet(CardTemplateSet.Default.Templates, new Dictionary<CardSlot, string> { [CardSlot.List] = "Ghost" });

        Assert.Equal(CardBuiltins.Classic, set.For(CardSlot.List));
        Assert.Equal(CardBuiltins.CalendarMonth, set.For(CardSlot.CalendarMonth));
    }

    [Fact]
    public void For_ReturnsTheAssignedTemplate() {
        var set = CardTemplateSet.Default.With(Custom()).Assign(CardSlot.CalendarDay, "Mine");

        Assert.Equal(Custom(), set.For(CardSlot.CalendarDay));
    }

    [Fact]
    public void With_AppendsNewAndReplacesByNameInPlace() {
        var added = CardTemplateSet.Default.With(Custom());
        var replaced = added.With(Custom() with { CustomCss = null });

        Assert.Equal([.. BuiltinNames, "Mine"], added.Templates.Select(t => t.Name));
        Assert.Equal(added.Templates.Count, replaced.Templates.Count);
        Assert.Null(replaced.Templates[^1].CustomCss);
    }

    [Fact]
    public void Without_RemovesCustomAndRemapsItsSlots() {
        var set = CardTemplateSet.Default.With(Custom()).Assign(CardSlot.List, "Mine").Assign(CardSlot.Agenda, "Mine");

        var removed = set.Without("Mine");

        Assert.DoesNotContain(removed.Templates, t => t.Name == "Mine");
        Assert.Equal("Classic", removed.Slots[CardSlot.List]);
        Assert.Equal("Agenda", removed.Slots[CardSlot.Agenda]);
        Assert.Equal(CardTemplateSet.Default, removed);
    }

    [Fact]
    public void Without_IgnoresBuiltinNames() {
        var set = CardTemplateSet.Default.Assign(CardSlot.List, "Agenda");

        Assert.Same(set, set.Without("Classic"));
        Assert.Same(set, set.Without("Agenda"));
    }

    [Fact]
    public void Assign_IgnoresUnknownNames() {
        var set = CardTemplateSet.Default;

        Assert.Same(set, set.Assign(CardSlot.List, "Ghost"));
        Assert.Equal("Agenda", set.Assign(CardSlot.List, "Agenda").Slots[CardSlot.List]);
    }

    [Fact]
    public void IsBuiltin_MatchesOnlyBuiltinNames() {
        Assert.All(BuiltinNames, n => Assert.True(CardTemplateSet.IsBuiltin(n)));
        Assert.False(CardTemplateSet.IsBuiltin("Mine"));
        Assert.False(CardTemplateSet.IsBuiltin("classic"));
    }

    [Fact]
    public void Equality_TreatsSlotsAsASet() {
        var a = new CardTemplateSet(CardTemplateSet.Default.Templates, new Dictionary<CardSlot, string> { [CardSlot.List] = "Classic", [CardSlot.Agenda] = "Agenda" });
        var b = new CardTemplateSet([.. CardTemplateSet.Default.Templates], new Dictionary<CardSlot, string> { [CardSlot.Agenda] = "Agenda", [CardSlot.List] = "Classic" });

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, a.Assign(CardSlot.List, "Agenda"));
    }

    [Fact]
    public void FromConfig_LaysEnabledFieldsInOrder() {
        var legacy = Legacy("Old", [
            (CardField.Target, true, 2),
            (CardField.ShipIcon, true, 0),
            (CardField.Duration, false, 1),
            (CardField.LevelStars, true, 3),
            (CardField.ShipName, true, 1),
            (CardField.CapacityModifierBadge, true, 4),
        ]);

        var template = LegacyCardUpgrade.FromConfig(legacy);
        var items = template.Doc.Items;

        Assert.Equal("Old", template.Name);
        Assert.Equal((24, 2), (template.Doc.Cols, template.Doc.Rows));
        Assert.Equal([CardKinds.ShipImage, CardKinds.Field, CardKinds.Field, CardKinds.Stars, CardKinds.CapBadge], items.Select(i => i.Kind));
        Assert.Equal([1, 3, 8, 13, 15], items.Select(i => i.X));
        Assert.Equal([2, 5, 5, 2, 2], items.Select(i => i.W));
        Assert.All(items, i => Assert.Equal(1, i.Y));
        Assert.Equal(MissionFields.Ship, CardPayload.Field(items[1]));
        Assert.Equal(MissionFields.Target, CardPayload.Field(items[2]));
    }

    [Fact]
    public void FromConfig_MapsFieldIds() {
        var legacy = Legacy("Ids", [
            (CardField.LaunchDate, true, 0),
            (CardField.LaunchTime, true, 1),
            (CardField.Duration, true, 2),
            (CardField.Capacity, true, 3),
            (CardField.DropCount, true, 4),
        ]);

        var items = LegacyCardUpgrade.FromConfig(legacy).Doc.Items;

        Assert.Equal(
            [MissionFields.LaunchDate, MissionFields.LaunchTime, MissionFields.Duration, MissionFields.Capacity, MissionFields.DropCount],
            items.Select(CardPayload.Field));
        Assert.Equal([3, 3, 2, 2, 2], items.Select(i => i.W));
    }

    [Fact]
    public void FromConfig_WrapsToRowTwo() {
        var legacy = Legacy("Row", [
            (CardField.LaunchDate, true, 0),
            (CardField.LaunchTime, true, 1),
            (CardField.ShipName, true, 2),
            (CardField.Target, true, 3),
            (CardField.ShipName, true, 4),
            (CardField.Target, true, 5),
        ]);

        var items = LegacyCardUpgrade.FromConfig(legacy).Doc.Items;

        Assert.Equal([1, 4, 7, 12, 17, 1], items.Select(i => i.X));
        Assert.Equal([1, 1, 1, 1, 1, 2], items.Select(i => i.Y));
    }

    [Fact]
    public void FromConfig_DropsAnythingNeedingRowThree() {
        var legacy = Legacy("Wide", [.. Enumerable.Range(0, 10).Select(i => (i % 2 == 0 ? CardField.ShipName : CardField.Target, true, i))]);

        var items = LegacyCardUpgrade.FromConfig(legacy).Doc.Items;

        Assert.Equal(8, items.Count);
        Assert.All(items, i => Assert.InRange(i.Y, 1, 2));
        Assert.Equal(items.Count, items.Select(i => i.Id).Distinct().Count());
    }

    [Fact]
    public void FromConfig_AddsColourBoxWhenBackgroundSet() {
        var legacy = Legacy("Coloured", "#101010", null, "#202020", [(CardField.ShipName, true, 0)]);

        var template = LegacyCardUpgrade.FromConfig(legacy);
        var box = template.Doc.Items[0];

        Assert.Equal(("bg", CardKinds.Box, 0), (box.Id, box.Kind, box.Z));
        Assert.Equal((1, 1, 24, 2), (box.X, box.Y, box.W, box.H));
        Assert.Equal("#101010", box.Style?.Background);
        Assert.Equal("#202020", box.Style?.Border);
        Assert.Equal(1, template.Doc.Items[1].Z);
    }

    [Fact]
    public void FromConfig_BorderOnlyStillAddsBox() {
        var legacy = Legacy("Bordered", null, "#303030", "#202020", [(CardField.ShipName, true, 0)]);

        var box = LegacyCardUpgrade.FromConfig(legacy).Doc.Items[0];

        Assert.Equal(CardKinds.Box, box.Kind);
        Assert.Equal("#303030", box.Style?.Border);
        Assert.Null(box.Style?.Background);
    }

    [Fact]
    public void FromConfig_NoColoursMeansNoBox() {
        var legacy = Legacy("Plain", null, null, "#202020", [(CardField.ShipName, true, 0)]);

        Assert.DoesNotContain(LegacyCardUpgrade.FromConfig(legacy).Doc.Items, i => i.Kind == CardKinds.Box);
    }

    [Fact]
    public void FromConfig_CarriesCssAndColorize() {
        var legacy = Legacy("Css", [(CardField.ShipName, true, 0)]) with { CustomCss = ".a{}", ColorizeByDuration = false };

        var template = LegacyCardUpgrade.FromConfig(legacy);

        Assert.Equal(".a{}", template.CustomCss);
        Assert.False(template.ColorizeByDuration);
    }

    [Fact]
    public void FromPresetSet_ConvertsPresetsAndSlotsTheActiveOne() {
        var mine = Legacy("Mine", [(CardField.ShipName, true, 0)]);
        var legacy = new CardPresetSet([MissionCardConfig.Classic, mine], "Mine");

        var set = LegacyCardUpgrade.FromPresetSet(legacy);

        Assert.Equal(CardBuiltins.Classic, set.Templates.Single(t => t.Name == "Classic"));
        Assert.Equal(LegacyCardUpgrade.FromConfig(mine), set.Templates.Single(t => t.Name == "Mine"));
        Assert.All(BuiltinNames, n => Assert.Contains(set.Templates, t => t.Name == n));
        Assert.Equal("Mine", set.Slots[CardSlot.List]);
        Assert.Equal("CalendarDay", set.Slots[CardSlot.CalendarDay]);
        Assert.Equal("Agenda", set.Slots[CardSlot.Agenda]);
    }

    [Fact]
    public void FromPresetSet_UnknownActiveFallsBackToClassic() {
        var set = LegacyCardUpgrade.FromPresetSet(new CardPresetSet([MissionCardConfig.Classic], "Gone"));

        Assert.Equal("Classic", set.Slots[CardSlot.List]);
        Assert.Equal(CardTemplateSet.Default, set);
    }
}
