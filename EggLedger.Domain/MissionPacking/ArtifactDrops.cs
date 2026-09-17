using EggLedger.Domain.Ei;
using EggLedger.Domain.Eiafx;
using Ei;

namespace EggLedger.Domain.MissionPacking;

public readonly record struct ArtifactDrop(
    int DropIndex,
    int ArtifactId,
    string SpecType,
    int Level,
    int Rarity,
    double Quality);

public static class ArtifactDrops {
    public static List<ArtifactDrop> Build(CompleteMissionResponse resp) {
        ArgumentNullException.ThrowIfNull(resp);
        var artifacts = resp.Artifacts;
        var drops = new List<ArtifactDrop>(artifacts.Count);
        for (int i = 0; i < artifacts.Count; i++) {
            var spec = artifacts[i].Spec;
            if (spec is null) {
                continue;
            }
            string name = EnumNames.ProtoName(spec.name);
            string specType = name.Contains("_FRAGMENT", StringComparison.Ordinal)
                ? "StoneFragment"
                : name.Contains("_STONE", StringComparison.Ordinal)
                    ? "Stone"
                    : name.Contains("GOLD_METEORITE", StringComparison.Ordinal)
                       || name.Contains("SOLAR_TITANIUM", StringComparison.Ordinal)
                       || name.Contains("TAU_CETI_GEODE", StringComparison.Ordinal)
                    ? "Ingredient"
                    : "Artifact";
            drops.Add(new ArtifactDrop(
                DropIndex: i,
                ArtifactId: (int)spec.name,
                SpecType: specType,
                Level: (int)spec.level,
                Rarity: (int)spec.rarity,
                Quality: Quality.BaseQualityFor(spec)));
        }
        return drops;
    }
}
