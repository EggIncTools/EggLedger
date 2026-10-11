using System.Text.Json;
using EggLedger.Web.Missions.Model;

namespace EggLedger.Web.Tests.Missions;

public class MissionCardConfigTests {
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Classic_HasCurrentDefaultFieldsEnabledInOriginalOrder() {
        var classic = MissionCardConfig.Classic;
        Assert.Equal(CardLayout.CompactRow, classic.Layout);
        Assert.False(classic.UsesCards);
        Assert.True(classic.ColorizeByDuration);
        Assert.Null(classic.CustomCss);

        var enabledInOrder = classic.Fields
            .Where(f => f.Enabled)
            .OrderBy(f => f.Order)
            .Select(f => f.Field)
            .ToArray();

        Assert.Equal(
            [CardField.LaunchDate, CardField.LaunchTime, CardField.ShipIcon, CardField.ShipName, CardField.LevelStars, CardField.Target, CardField.DropCount],
            enabledInOrder);
    }

    [Fact]
    public void WithAllFields_AppendsMissingFieldsEnabledAfterExistingOrder() {
        var legacy = MissionCardConfig.Classic with {
            Fields = [new CardFieldSlot(CardField.ShipName, true, 0), new CardFieldSlot(CardField.Target, false, 1)]
        };

        var filled = legacy.WithAllFields();

        Assert.Equal(Enum.GetValues<CardField>().Length, filled.Fields.Count);
        Assert.Equal([CardField.ShipName, CardField.Target], filled.Fields.OrderBy(f => f.Order).Take(2).Select(f => f.Field));
        Assert.All(filled.Fields.Skip(2), f => Assert.True(f.Enabled));
        Assert.Equal(filled.Fields.Count, filled.Fields.Select(f => f.Order).Distinct().Count());
        Assert.Same(filled, filled.WithAllFields());
    }

    [Fact]
    public void ActivePreset_FillsMissingFields() {
        var partial = MissionCardConfig.Classic with { Name = "Old", Fields = [new CardFieldSlot(CardField.ShipName, true, 0)] };
        var set = new CardPresetSet([MissionCardConfig.Classic, partial], "Old");

        Assert.Contains(set.ActivePreset().Fields, f => f.Field == CardField.ShipIcon && f.Enabled);
    }

    [Fact]
    public void CalendarConfigs_UseCardsAndCarryNoUserStyling() {
        foreach (var c in new[] { MissionCardConfig.CalendarDay, MissionCardConfig.CalendarWeek, MissionCardConfig.CalendarMonth, MissionCardConfig.Agenda }) {
            Assert.True(c.UsesCards);
            Assert.Null(c.CustomCss);
            Assert.Null(c.BackgroundColor);
            Assert.All(c.Fields, f => Assert.True(f.Enabled));
        }
    }

    [Fact]
    public void MissionCardConfig_JsonRoundtrips() {
        var config = MissionCardConfig.Classic with { Name = "Custom", BackgroundColor = "#112233" };
        var json = JsonSerializer.Serialize(config, JsonOpts);
        var back = JsonSerializer.Deserialize<MissionCardConfig>(json, JsonOpts);
        Assert.Equal(config, back);
    }

    [Fact]
    public void CardPresetSet_Default_HasOnlyClassicAndItIsActive() {
        var set = CardPresetSet.Default;
        Assert.Single(set.Presets);
        Assert.Equal("Classic", set.ActivePresetName);
        Assert.Equal(MissionCardConfig.Classic, set.Presets[0]);
    }

    [Fact]
    public void CardPresetSet_JsonRoundtrips() {
        var set = new CardPresetSet([MissionCardConfig.Classic, MissionCardConfig.Classic with { Name = "Alt" }], "Alt");
        var json = JsonSerializer.Serialize(set, JsonOpts);
        var back = JsonSerializer.Deserialize<CardPresetSet>(json, JsonOpts);
        Assert.Equal(set, back);
    }

    [Fact]
    public void Classic_ShowsExpectedDropsPerShipByDefault() =>
        Assert.True(MissionCardConfig.Classic.ShowExpectedDropsPerShip);
}
