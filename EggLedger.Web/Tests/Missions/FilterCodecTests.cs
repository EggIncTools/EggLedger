using EggLedger.Web.Missions.Model;
using WebCondition = EggLedger.Web.Missions.FilterCondition;

namespace EggLedger.Web.Tests.Missions;

public class FilterCodecTests {
    [Theory]
    [InlineData("ship", "=", "2", FilterField.Ship)]
    [InlineData("level", ">", "5", FilterField.Level)]
    [InlineData("launchDT", "d=", "2024-06-01", FilterField.LaunchDate)]
    [InlineData("dubcap", "=", "true", FilterField.DubCap)]
    public void FromLegacyCondition_ParsesKnownFields(string topLevel, string op, string val, FilterField expected) {
        var parsed = FilterCodec.FromLegacyCondition(new WebCondition(topLevel, op, val));
        Assert.NotNull(parsed);
        Assert.Equal(expected, parsed!.Field);
    }

    [Theory]
    [InlineData("", "=", "1")]
    [InlineData("nonsense", "=", "1")]
    [InlineData("ship", "=", "notanint")]
    public void FromLegacyCondition_ReturnsNull_ForUnknownOrUnparsable(string topLevel, string op, string val) {
        Assert.Null(FilterCodec.FromLegacyCondition(new WebCondition(topLevel, op, val)));
    }

    [Theory]
    [InlineData("%_%_3_%")]
    [InlineData("40_2_1_9")]
    public void DropGlob_Roundtrip(string glob) {
        var m = FilterCodec.DecodeDropGlob(glob);
        Assert.Equal(glob, FilterCodec.EncodeDropGlob(m));
    }

    [Fact]
    public void DropGlob_Roundtrip_Wildcards_ParsesSegments() {
        var m = FilterCodec.DecodeDropGlob("%_%_3_%");
        Assert.Null(m.Name);
        Assert.Null(m.Level);
        Assert.Equal(3, m.Rarity);
        Assert.Null(m.Quality);
    }

    [Fact]
    public void DropGlob_Roundtrip_Exact_ParsesSegments() {
        var m = FilterCodec.DecodeDropGlob("40_2_1_9");
        Assert.Equal(40, m.Name);
        Assert.Equal(2, m.Level);
        Assert.Equal(1, m.Rarity);
        Assert.Equal(9d, m.Quality);
    }
}
