using System.Globalization;
using EggIdentity.Filters;

namespace EggLedger.Domain.Reports;

public static class LedgerFilters {
    public const string MissionScope = "mission";
    public const string ArtifactScope = "artifact";

    public static IReadOnlyList<FilterOpDef> DateOps { get; } = [.. FilterOps.Date, new(FilterOps.DayEq, "on")];

    public static IReadOnlyList<FilterFieldDef> Fields { get; } = [
        new("ship", "Ship", FilterValueKind.Select, FilterOps.Comparison) { Scope = MissionScope },
        new("duration", "Duration", FilterValueKind.Select, FilterOps.Comparison) { Scope = MissionScope },
        new("level", "Level", FilterValueKind.Select, FilterOps.Comparison) { Scope = MissionScope },
        new("target", "Target", FilterValueKind.SearchableSelect, FilterOps.Equality) { Scope = MissionScope },
        new("type", "Mission Type", FilterValueKind.Select, FilterOps.Equality) { Scope = MissionScope, Aliases = ["farm"] },
        new("launchDT", "Launch Date", FilterValueKind.Date, DateOps) { Scope = MissionScope },
        new("returnDT", "Return Date", FilterValueKind.Date, DateOps) { Scope = MissionScope },
        new("dubcap", "Dub cap", FilterValueKind.Bool, FilterOps.Bool) { Scope = MissionScope },
        new("buggedcap", "Bugged cap", FilterValueKind.Bool, FilterOps.Bool) { Scope = MissionScope },
        new("drops", "Drops", FilterValueKind.Set, FilterOps.ContainsOps) { Scope = MissionScope, Compare = MatchDrops },
        new("artifact_name", "Name", FilterValueKind.SearchableSelect, FilterOps.Equality) { Scope = ArtifactScope },
        new("artifact_rarity", "Rarity", FilterValueKind.Select, FilterOps.Comparison) { Scope = ArtifactScope },
        new("artifact_tier", "Tier", FilterValueKind.Select, FilterOps.Comparison) { Scope = ArtifactScope },
        new("artifact_spec_type", "Spec Type", FilterValueKind.Select, FilterOps.Equality) { Scope = ArtifactScope },
        new("artifact_quality", "Quality", FilterValueKind.Number, FilterOps.Comparison) { Scope = ArtifactScope },
    ];

    public static FilterFieldDef? Find(string key) => FilterFields.Find(Fields, key);

    public static bool IsArtifactField(string key) => Find(key)?.Scope == ArtifactScope;

    public static IEnumerable<FilterCondition> Conditions(FilterSet set) => [.. set.And, .. set.Or.SelectMany(g => g)];

    public static IEnumerable<IReadOnlyList<FilterCondition>> Rows(FilterSet set) =>
        set.And.Select((head, i) => (IReadOnlyList<FilterCondition>)[head, .. i < set.Or.Count ? set.Or[i] : []]);

    public static string DropKey(long artifactId, long level, long rarity) =>
        string.Create(CultureInfo.InvariantCulture, $"{artifactId}_{level}_{rarity}");

    public static bool DropMatchesGlob(string dropKey, string glob) {
        var want = (glob ?? "").Split('_');
        var have = dropKey.Split('_');
        for (var i = 0; i < 3; i++) {
            if (i >= want.Length || want[i] is "%" or "") continue;
            if (i >= have.Length || !string.Equals(want[i], have[i], StringComparison.Ordinal)) return false;
        }
        return true;
    }

    private static bool? MatchDrops(FilterSubjectValue actual, string op, string value) {
        if (actual is not FilterSubjectValue.ManyValue many) return op == FilterOps.NotContains;
        var any = many.Values.Any(key => DropMatchesGlob(key, value));
        return op == FilterOps.NotContains ? !any : any;
    }
}
