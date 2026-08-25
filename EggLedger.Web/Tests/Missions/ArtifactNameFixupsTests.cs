using EggLedger.Web.Missions;

namespace EggLedger.Web.Tests.Missions;

public sealed class ArtifactNameFixupsTests {
    [Theory]
    [InlineData("ORNATE_GUSSET", "GUSSET")]
    [InlineData("VIAL_MARTIAN_DUST", "VIAL_OF_MARTIAN_DUST")]
    [InlineData("ORNATE_GUSSET_FRAGMENT", "GUSSET_FRAGMENT")]
    [InlineData("LUNAR_TOTEM", "LUNAR_TOTEM")]
    public void ApplyDisplayNameOverrides_RewritesKnownNames(string input, string expected) {
        Assert.Equal(expected, ArtifactNameFixups.ApplyDisplayNameOverrides(input));
    }
}
