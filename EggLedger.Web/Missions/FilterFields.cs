using EggLedger.Domain.MissionQuery;

namespace EggLedger.Web.Missions;

public sealed class FilterFieldCtx {
    public IReadOnlyList<PossibleTarget> PossibleTargets { get; init; } = [];
    public IReadOnlyList<PossibleArtifact> ArtifactConfigs { get; init; } = [];
    public double MaxQuality { get; init; }
}

public sealed class FilterFieldDef {
    public string Key { get; init; } = "";
    public string Label { get; init; } = "";

    public string Scope { get; init; } = "mission";
    public FilterValueKind ValueKind { get; init; }
    public IReadOnlyList<FilterOp> Ops { get; init; } = [];

    public Func<FilterFieldCtx, List<FilterOption>>? OptionsSource { get; init; }
}

public static class FilterFields {
    public static readonly IReadOnlyList<FilterOp> EqualityOps = [
        new FilterOp("=", "is"),
        new FilterOp("!=", "is not"),
    ];

    public static readonly IReadOnlyList<FilterOp> ComparisonOps = [
        new FilterOp("=", "is"),
        new FilterOp("!=", "is not"),
        new FilterOp(">", "greater than"),
        new FilterOp("<", "less than"),
        new FilterOp(">=", "at least"),
        new FilterOp("<=", "at most"),
    ];

    public static readonly IReadOnlyList<FilterOp> DateOps = [
        new FilterOp("=", "on"),
        new FilterOp("<", "before"),
        new FilterOp(">", "after"),
        new FilterOp("<=", "on or before"),
        new FilterOp(">=", "on or after"),
    ];

    public static readonly IReadOnlyList<FilterOp> MissionDateOps = [
        new FilterOp("d=", "on"),
        new FilterOp("<", "before"),
        new FilterOp(">", "after"),
    ];

    public static IReadOnlyList<FilterOp> MissionBarOpsFor(FilterFieldDef def) =>
        def.ValueKind == FilterValueKind.Date ? MissionDateOps : def.Ops;

    public static readonly IReadOnlyList<FilterOp> DropsOps = [
        new FilterOp("c", "contains"),
        new FilterOp("dnc", "does not contain"),
    ];

    public static readonly IReadOnlyList<FilterOp> BoolOps = [
        new FilterOp("true", "True"),
        new FilterOp("false", "False"),
    ];

    public static readonly IReadOnlyList<FilterFieldDef> ReportFilterFields = [
        new FilterFieldDef {
            Key = "ship", Label = "Ship", Scope = "mission", ValueKind = FilterValueKind.Select,
            Ops = ComparisonOps, OptionsSource = _ => FilterOptions.GetMissionFilterValueOptions("ship"),
        },
        new FilterFieldDef {
            Key = "duration", Label = "Duration", Scope = "mission", ValueKind = FilterValueKind.Select,
            Ops = ComparisonOps, OptionsSource = _ => FilterOptions.GetMissionFilterValueOptions("duration"),
        },
        new FilterFieldDef {
            Key = "level", Label = "Level", Scope = "mission", ValueKind = FilterValueKind.Select,
            Ops = ComparisonOps, OptionsSource = _ => FilterOptions.GetMissionFilterValueOptions("level"),
        },
        new FilterFieldDef {
            Key = "target", Label = "Target", Scope = "mission", ValueKind = FilterValueKind.Modal,
            Ops = EqualityOps, OptionsSource = ctx => FilterOptions.GetTargetFilterOptions(ctx.PossibleTargets),
        },
        new FilterFieldDef {
            Key = "type", Label = "Mission Type", Scope = "mission", ValueKind = FilterValueKind.Select,
            Ops = EqualityOps, OptionsSource = _ => FilterOptions.GetMissionFilterValueOptions("type"),
        },
        new FilterFieldDef {
            Key = "launchDT", Label = "Launch Date", Scope = "mission", ValueKind = FilterValueKind.Date,
            Ops = DateOps,
        },
        new FilterFieldDef {
            Key = "returnDT", Label = "Return Date", Scope = "mission", ValueKind = FilterValueKind.Date,
            Ops = DateOps,
        },
        new FilterFieldDef {
            Key = "dubcap", Label = "Dub cap", Scope = "mission", ValueKind = FilterValueKind.Bool,
            Ops = BoolOps,
        },
        new FilterFieldDef {
            Key = "buggedcap", Label = "Bugged cap", Scope = "mission", ValueKind = FilterValueKind.Bool,
            Ops = BoolOps,
        },
        new FilterFieldDef {
            Key = "drops", Label = "Drops", Scope = "mission", ValueKind = FilterValueKind.Modal,
            Ops = DropsOps,
            OptionsSource = ctx => FilterOptions.GetDropFilterOptions(ctx.ArtifactConfigs, ctx.MaxQuality, true),
        },
        new FilterFieldDef {
            Key = "artifact_name", Label = "Name", Scope = "artifact", ValueKind = FilterValueKind.Modal,
            Ops = EqualityOps, OptionsSource = ctx => FilterOptions.GetArtifactNameFilterOptions(ctx.ArtifactConfigs),
        },
        new FilterFieldDef {
            Key = "artifact_rarity", Label = "Rarity", Scope = "artifact", ValueKind = FilterValueKind.Select,
            Ops = ComparisonOps, OptionsSource = _ => FilterOptions.GetArtifactRarityFilterOptions(),
        },
        new FilterFieldDef {
            Key = "artifact_tier", Label = "Tier", Scope = "artifact", ValueKind = FilterValueKind.Select,
            Ops = ComparisonOps, OptionsSource = ctx => FilterOptions.GetArtifactTierFilterOptions(ctx.ArtifactConfigs),
        },
        new FilterFieldDef {
            Key = "artifact_spec_type", Label = "Spec Type", Scope = "artifact", ValueKind = FilterValueKind.Select,
            Ops = EqualityOps, OptionsSource = _ => FilterOptions.GetArtifactSpecTypeFilterOptions(),
        },
        new FilterFieldDef {
            Key = "artifact_quality", Label = "Quality", Scope = "artifact", ValueKind = FilterValueKind.Number,
            Ops = ComparisonOps,
        },
    ];

    public static FilterFieldDef? GetReportField(string key) {
        foreach (var f in ReportFilterFields) {
            if (f.Key == key) {
                return f;
            }
        }
        return null;
    }

    public static List<FilterFieldDef> ReportMissionFields() {
        var result = new List<FilterFieldDef>();
        foreach (var f in ReportFilterFields) {
            if (f.Scope == "mission") {
                result.Add(f);
            }
        }
        return result;
    }

    public static List<FilterFieldDef> ReportArtifactFields() {
        var result = new List<FilterFieldDef>();
        foreach (var f in ReportFilterFields) {
            if (f.Scope == "artifact") {
                result.Add(f);
            }
        }
        return result;
    }
}
