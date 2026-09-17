using EggLedger.Domain.Ei;
using EggLedger.Domain.Eiafx;
using Ei;

namespace EggLedger.Domain.MissionQuery;

public sealed class PossibleTarget {
    public string DisplayName { get; set; } = "";

    public int Id { get; set; }
    public string ImageString { get; set; } = "";
}

public sealed class PossibleArtifact {
    public int Name { get; set; }
    public string ProtoName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public int Level { get; set; }
    public int Rarity { get; set; }
    public double BaseQuality { get; set; }
}

public static class MissionConfigData {
    public static double MaxQuality() {
        double maxQuality = 0;
        foreach (var mission in EiafxConfig.Config.mission_parameters) {
            int maxLevels = mission.LevelMissionRequirements?.Length ?? 0;
            foreach (var d in mission.Durations) {
                double comped = d.MaxQuality + (d.LevelQualityBump * maxLevels);
                if (comped > maxQuality) {
                    maxQuality = comped;
                }
            }
        }
        return maxQuality;
    }

    public static List<PossibleTarget> Targets() {
        var result = new List<PossibleTarget>
        {
            new() { DisplayName = "None (Pre 1.27)", Id = -1, ImageString = "none.png" },
        };
        foreach (var target in LedgerData.LedgerData.Config.ArtifactTargets) {
            int id = EnumNames.TryValue<ArtifactSpec.Name>(target.Name, out var val) ? (int)val : 0;
            result.Add(new PossibleTarget {
                DisplayName = target.DisplayName,
                Id = id,
                ImageString = target.ImageString,
            });
        }
        return result;
    }

    public static List<PossibleArtifact> Artifacts() {
        double maxQuality = MaxQuality();
        var result = new List<PossibleArtifact>();
        foreach (var artifact in EiafxConfig.Config.artifact_parameters) {
            var spec = artifact.Spec;
            if (spec is null) {
                continue;
            }
            if (maxQuality >= artifact.BaseQuality) {
                result.Add(new PossibleArtifact {
                    Name = (int)spec.name,
                    ProtoName = EnumNames.ProtoName(spec.name),
                    DisplayName = spec.CasedSmallName(),
                    Level = (int)spec.level,
                    Rarity = (int)spec.rarity,
                    BaseQuality = artifact.BaseQuality,
                });
            }
        }
        return result;
    }
}
