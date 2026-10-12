using EggLedger.Web.Missions;

namespace EggLedger.Web.Tests.Missions;

public class FilterCodecTests {
    [Theory]
    [InlineData("%_%_3_%")]
    [InlineData("40_2_1_9")]
    public void DropGlob_Roundtrip(string glob) =>
        Assert.Equal(glob, DropMatch.Decode(glob).Encode());

    [Fact]
    public void DropGlob_Wildcards_ParsesSegments() {
        var m = DropMatch.Decode("%_%_3_%");
        Assert.Null(m.Name);
        Assert.Null(m.Level);
        Assert.Equal(3, m.Rarity);
        Assert.Null(m.Quality);
    }

    [Fact]
    public void DropGlob_Exact_ParsesSegments() {
        var m = DropMatch.Decode("40_2_1_9");
        Assert.Equal(40, m.Name);
        Assert.Equal(2, m.Level);
        Assert.Equal(1, m.Rarity);
        Assert.Equal(9d, m.Quality);
    }

    [Fact]
    public void StoredReportJson_RoundTripsUnchanged() {
        const string json = """{"and":[{"topLevel":"dubcap","op":"true","val":""},{"topLevel":"ship","op":"eq","val":"3"}],"or":[[],[{"topLevel":"farm","op":"=","val":"1"}]]}""";
        Assert.Equal(json, FilterCodec.Serialize(FilterCodec.Deserialize(json)));
    }
}
