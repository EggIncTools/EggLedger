using System.Globalization;
using System.Linq;
using Ei;
using EggLedger.Domain.Ei;
using EggLedger.Domain.MissionQuery;

namespace EggLedger.Web.Missions;

public static class FilterOptions {
    public static List<FilterOption> GetShipFilterOptions() {
        var result = new List<FilterOption>();
        foreach (var s in Enum.GetValues<MissionInfo.Spaceship>().OrderBy(s => (int)s)) {
            result.Add(new FilterOption { Text = s.Name(), Value = ((int)s).ToString(CultureInfo.InvariantCulture) });
        }
        return result;
    }

    public static List<FilterOption> GetDurationFilterOptions() {
        var result = new List<FilterOption>();
        foreach (var d in Enum.GetValues<MissionInfo.DurationType>().OrderBy(d => (int)d)) {
            int i = (int)d;
            result.Add(new FilterOption {
                Text = d.Display(),
                Value = i.ToString(CultureInfo.InvariantCulture),
                StyleClass = "text-duration-" + i,
            });
        }
        return result;
    }

    public static List<FilterOption> GetLevelFilterOptions() {
        var result = new List<FilterOption>(9);
        for (int i = 0; i < 9; i++) {
            result.Add(new FilterOption { Text = i + "★", Value = i.ToString(CultureInfo.InvariantCulture) });
        }
        return result;
    }

    public static List<FilterOption> GetMissionTypeFilterOptions() =>
    [
        new FilterOption { Text = "Home", Value = "0" },
        new FilterOption { Text = "Virtue", Value = "1" },
        new FilterOption { Text = "Unknown", Value = "-1" },
    ];

    public static List<FilterOption> GetFarmFilterOptions() =>
    [
        new FilterOption { Text = "Home", Value = "0" },
        new FilterOption { Text = "Virtue", Value = "1" },
    ];

    public static List<FilterOption> GetBoolFilterOptions() =>
    [
        new FilterOption { Text = "True", Value = "true" },
        new FilterOption { Text = "False", Value = "false" },
    ];

    public static List<FilterOption> GetTargetFilterOptions(IEnumerable<PossibleTarget> possibleTargets) {
        var result = new List<FilterOption>();
        foreach (var t in possibleTargets) {
            result.Add(new FilterOption {
                Text = t.DisplayName,
                Value = t.Id.ToString(CultureInfo.InvariantCulture),
                ImagePath = t.ImageString,
            });
        }
        return result;
    }

    public static List<FilterOption> GetArtifactRarityFilterOptions() =>
    [
        new FilterOption { Text = "Common", Value = "0" },
        new FilterOption { Text = "Rare", Value = "1", StyleClass = "text-rare" },
        new FilterOption { Text = "Epic", Value = "2", StyleClass = "text-epic" },
        new FilterOption { Text = "Legendary", Value = "3", StyleClass = "text-legendary" },
    ];

    public static List<FilterOption> GetArtifactSpecTypeFilterOptions() =>
    [
        new FilterOption { Text = "Artifact", Value = "0" },
        new FilterOption { Text = "Stone", Value = "1" },
        new FilterOption { Text = "Stone Fragment", Value = "2" },
        new FilterOption { Text = "Ingredient", Value = "3" },
    ];

    public static List<FilterOption> GetMissionFilterValueOptions(string topLevel) => topLevel switch {
        "ship" => GetShipFilterOptions(),
        "farm" => GetFarmFilterOptions(),
        "duration" => GetDurationFilterOptions(),
        "level" => GetLevelFilterOptions(),
        "type" => GetMissionTypeFilterOptions(),
        "dubcap" or "buggedcap" => GetBoolFilterOptions(),
        _ => [],
    };

    public static List<FilterOption> GetArtifactTierFilterOptions(IEnumerable<PossibleArtifact> artifactConfigs) {
        var levels = new SortedSet<int>();
        foreach (var a in artifactConfigs) {
            levels.Add(a.Level);
        }
        var result = new List<FilterOption>();
        foreach (var level in levels) {
            result.Add(new FilterOption {
                Text = "Tier " + (level + 1),
                Value = level.ToString(CultureInfo.InvariantCulture),
            });
        }
        return result;
    }

    public static List<FilterOption> GetArtifactNameFilterOptions(IReadOnlyList<PossibleArtifact> artifactConfigs) {
        var representatives = new Dictionary<int, PossibleArtifact>();
        foreach (var a in artifactConfigs) {
            if (!representatives.TryGetValue(a.Name, out var existing) || a.Level < existing.Level) {
                representatives[a.Name] = a;
            }
        }
        var result = new List<FilterOption>();
        foreach (var a in representatives.Values) {
            result.Add(new FilterOption {
                Text = a.DisplayName,
                Value = a.Name.ToString(CultureInfo.InvariantCulture),
                ImagePath = DropPath(a),
            });
        }

        result.Sort((x, y) => string.Compare(x.Text, y.Text, StringComparison.OrdinalIgnoreCase));
        return result;
    }

    public static string? RarityStyleClass(int rarity) => rarity switch {
        1 => "text-rare",
        2 => "text-epic",
        3 => "text-legendary",
        _ => null
    };

    private static readonly string[] RarityWords = ["Common", "Rare", "Epic", "Legendary"];

    public static string RarityWord(int rarity) =>
        rarity >= 0 && rarity < RarityWords.Length ? RarityWords[rarity] : "";

    private static string ArtifactDisplayText(PossibleArtifact artifact) {
        return artifact.DisplayName + " (T" + TierNumber(artifact) + ")";
    }

    private static int TierNumber(PossibleArtifact artifact) {
        bool isStoneNotFragment =
            artifact.DisplayName.Contains("stone", StringComparison.OrdinalIgnoreCase) &&
            !artifact.DisplayName.Contains("fragment", StringComparison.OrdinalIgnoreCase);
        return artifact.Level + (isStoneNotFragment ? 2 : 1);
    }

    private static string DropPath(PossibleArtifact drop) {
        int addendum = drop.ProtoName.Contains("_STONE", StringComparison.Ordinal) ? 1 : 0;
        string fixedName = ArtifactNameFixups.ApplyDisplayNameOverrides(
            drop.ProtoName.Replace("_FRAGMENT", "", StringComparison.Ordinal));
        return "artifacts/" + fixedName + "/" + fixedName + "_" + (drop.Level + 1 + addendum) + ".png";
    }

    public static List<FilterOption> GetDropFilterOptions(
        IReadOnlyList<PossibleArtifact> artifactConfigs,
        double maxQuality,
        bool advanced) {
        var artifactList = new List<PossibleArtifact>();
        foreach (var a in artifactConfigs) {
            if (a.BaseQuality <= maxQuality) {
                artifactList.Add(a);
            }
        }

        var result = new List<FilterOption>
        {
            new() { Text = "Any Rare", Value = "%_%_1_%", Rarity = 1, StyleClass = "text-rare", ImagePath = "icon_help.webp" },
            new() { Text = "Any Epic", Value = "%_%_2_%", Rarity = 2, StyleClass = "text-epic", ImagePath = "icon_help.webp" },
            new() { Text = "Any Legendary", Value = "%_%_3_%", Rarity = 3, StyleClass = "text-legendary", ImagePath = "icon_help.webp" },
        };




        var stoneProtoNames = new HashSet<string>();
        foreach (var a in artifactList) {
            if (!a.ProtoName.Contains("_FRAGMENT", StringComparison.Ordinal)) {
                stoneProtoNames.Add(a.ProtoName);
            }
        }
        var canonicalFamily = new Dictionary<int, int>();
        foreach (var a in artifactList) {
            if (a.ProtoName.Contains("_FRAGMENT", StringComparison.Ordinal)) {
                var stoneProtoName = a.ProtoName.Replace("_FRAGMENT", "", StringComparison.Ordinal);
                if (stoneProtoNames.Contains(stoneProtoName)) {
                    foreach (var candidate in artifactList) {
                        if (candidate.ProtoName == stoneProtoName) {
                            canonicalFamily[a.Name] = candidate.Name;
                            break;
                        }
                    }
                }
            }
        }


        var byFamily = new Dictionary<int, Dictionary<int, List<PossibleArtifact>>>();
        var familyOrder = new List<int>();
        foreach (var a in artifactList) {
            var familyId = canonicalFamily.GetValueOrDefault(a.Name, a.Name);
            if (!byFamily.TryGetValue(familyId, out var byTier)) {
                byTier = [];
                byFamily[familyId] = byTier;
                familyOrder.Add(familyId);
            }
            var tier = TierNumber(a);
            if (!byTier.TryGetValue(tier, out var rarities)) {
                rarities = [];
                byTier[tier] = rarities;
            }
            rarities.Add(a);
        }

        foreach (var familyId in familyOrder) {
            var tierMap = byFamily[familyId];
            var tierOrder = tierMap.Keys.Order().ToList();
            var groupKey = familyId.ToString(CultureInfo.InvariantCulture);
            var familyDisplayName = FamilyDisplayName(tierMap, tierOrder);
            var totalCount = 0;
            foreach (var rarities in tierMap.Values) {
                totalCount += rarities.Count;
            }

            if (advanced && totalCount > 1) {
                result.Add(new FilterOption {
                    Text = familyDisplayName + " (Any)",
                    Value = familyId + "_%_%_%",
                    Rarity = null,
                    ImagePath = "icon_help.webp",
                    Badge = "Any",
                    GroupKey = groupKey,
                    GroupLabel = familyDisplayName,
                });
            }

            foreach (var tierLevel in tierOrder) {
                var rarities = tierMap[tierLevel];
                var tierKey = groupKey + "_" + tierLevel;
                if (advanced && rarities.Exists(a => a.Rarity > 0)) {
                    result.Add(new FilterOption {
                        Text = ArtifactDisplayText(rarities[0]) + " (Any Rarity)",
                        Value = rarities[0].Name + "_" + rarities[0].Level + "_%_%",
                        Rarity = null,
                        ImagePath = "icon_help.webp",
                        Badge = "T" + tierLevel + " Any",
                        GroupKey = groupKey,
                        GroupLabel = familyDisplayName,
                        TierKey = tierKey,
                    });
                }
                foreach (var a in rarities) {
                    result.Add(new FilterOption {
                        Text = ArtifactDisplayText(a),
                        Value = a.Name + "_" + a.Level + "_" + a.Rarity + "_" + a.BaseQuality.ToString(CultureInfo.InvariantCulture),
                        Rarity = a.Rarity,
                        StyleClass = RarityStyleClass(a.Rarity),
                        ImagePath = DropPath(a),
                        Badge = "T" + tierLevel,
                        GroupKey = groupKey,
                        GroupLabel = familyDisplayName,
                        TierKey = tierKey,
                    });
                }
            }
        }

        return result;
    }



    private static string FamilyDisplayName(Dictionary<int, List<PossibleArtifact>> tierMap, List<int> tierOrder) {
        foreach (var tier in tierOrder) {
            foreach (var a in tierMap[tier]) {
                if (!a.DisplayName.Contains("fragment", StringComparison.OrdinalIgnoreCase)) {
                    return a.DisplayName;
                }
            }
        }
        return tierMap[tierOrder[0]][0].DisplayName;
    }
}
