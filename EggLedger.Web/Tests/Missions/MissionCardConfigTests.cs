using System.Text.Json;
using EggLedger.Web.Missions.Model;
using Xunit;

namespace EggLedger.Web.Tests.Missions;

public class MissionCardConfigTests {
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Classic_HasCurrentDefaultFieldsEnabledInOriginalOrder() {
        var classic = MissionCardConfig.Classic;
        Assert.Equal(CardLayout.CompactRow, classic.Layout);
        Assert.True(classic.ColorizeByDuration);
        Assert.Null(classic.CustomCss);

        var enabledInOrder = classic.Fields
            .Where(f => f.Enabled)
            .OrderBy(f => f.Order)
            .Select(f => f.Field)
            .ToArray();

        Assert.Equal(
            [CardField.LaunchDate, CardField.LaunchTime, CardField.ShipName, CardField.LevelStars, CardField.Target, CardField.DropCount],
            enabledInOrder);
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
    public void Classic_ShowsExpectedDropsPerShipByDefault() {
        Assert.True(MissionCardConfig.Classic.ShowExpectedDropsPerShip);
    }
}
