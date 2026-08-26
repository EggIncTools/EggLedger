using EggLedger.Web.Ships;
using Ei;

namespace EggLedger.Web.Tests.Ships;

public sealed class ShipAssetKeyTests {
    [Theory]
    [InlineData(MissionInfo.Spaceship.ChickenOne, "ChickenOne")]
    [InlineData(MissionInfo.Spaceship.Bcr, "Bcr")]
    [InlineData(MissionInfo.Spaceship.MilleniumChicken, "MilleniumChicken")]
    [InlineData(MissionInfo.Spaceship.Henerprise, "Henerprise")]
    [InlineData(MissionInfo.Spaceship.Atreggies, "Atreggies")]
    public void For_ReturnsEnumMemberName(MissionInfo.Spaceship ship, string expected) {
        Assert.Equal(expected, ShipAssetKey.For(ship));
    }

    [Fact]
    public void AllKeys_CoversAllElevenMembers() {
        Assert.Equal(11, ShipAssetKey.AllKeys.Count);
        Assert.Contains("CorellihenCorvette", ShipAssetKey.AllKeys);
        Assert.Contains("Chickfiant", ShipAssetKey.AllKeys);
    }

    [Theory]
    [InlineData("Henerprise", true)]
    [InlineData("CHICKEN_ONE", false)]
    [InlineData("../etc/passwd", false)]
    [InlineData("", false)]
    public void IsKnown_AcceptsOnlyDefinedMemberNames(string key, bool expected) {
        Assert.Equal(expected, ShipAssetKey.IsKnown(key));
    }
}
