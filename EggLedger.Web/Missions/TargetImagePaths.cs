using EggLedger.Web.State;

namespace EggLedger.Web.Missions;

public static class TargetImagePaths {
    public static (string? Path, string Alt) Resolve(string? target) {
        if (string.IsNullOrEmpty(target) || target == "UNKNOWN") {
            return (null, "");
        }

        var t = ArtifactNameFixups.ApplyDisplayNameOverrides(target.ToUpperInvariant());
        var tier = 4;
        if (t.Contains("_FRAGMENT", StringComparison.Ordinal)) {
            t = t.Replace("_FRAGMENT", "", StringComparison.Ordinal);
            tier = 1;
        }

        if (t is "GOLD_METEORITE" or "TAU_CETI_GEODE" or "SOLAR_TITANIUM") {
            tier = 3;
        }

        return (ContentPaths.Asset($"images/artifacts/{t}/{t}_{tier}.png"), t);
    }
}
