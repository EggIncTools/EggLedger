using EggLedger.Domain.MissionQuery;
using EggLedger.Domain.Reports;

namespace EggLedger.Web.Missions;

public sealed class FilterFieldCtx {
    public IReadOnlyList<PossibleTarget> PossibleTargets { get; init; } = [];
    public IReadOnlyList<PossibleArtifact> ArtifactConfigs { get; init; } = [];
    public double MaxQuality { get; init; }
}

public static class LedgerFilterFields {
    public static IReadOnlyList<FilterFieldDef> ReportFilterFields => LedgerFilters.Fields;

    public static IReadOnlyList<FilterFieldDef> ReportMissionFields { get; } =
        [.. LedgerFilters.Fields.Where(f => f.Scope == LedgerFilters.MissionScope)];

    public static IReadOnlyList<FilterOpDef> MissionBarOpsFor(FilterFieldDef def) =>
        def.Kind == FilterValueKind.Date ? FilterOps.DayDate : ReportOpsFor(def);

    public static IReadOnlyList<FilterOpDef> ReportOpsFor(FilterFieldDef def) =>
        def.Kind == FilterValueKind.Date ? FilterOps.Date : def.Ops;

    public static bool UsesPicker(FilterFieldDef def) => def.Key is "target" or "drops" or "artifact_name";

    public static List<PickerOption> PickerOptionsFor(FilterFieldDef def, FilterFieldCtx ctx, bool advancedDrops) => def.Key switch {
        "ship" or "duration" or "level" or "type" or "dubcap" or "buggedcap" => FilterOptions.GetMissionFilterValueOptions(def.Key),
        "target" => FilterOptions.GetTargetFilterOptions(ctx.PossibleTargets),
        "drops" => FilterOptions.GetDropFilterOptions(ctx.ArtifactConfigs, ctx.MaxQuality, advancedDrops),
        "artifact_name" => FilterOptions.GetArtifactNameFilterOptions(ctx.ArtifactConfigs),
        "artifact_rarity" => FilterOptions.GetArtifactRarityFilterOptions(),
        "artifact_tier" => FilterOptions.GetArtifactTierFilterOptions(ctx.ArtifactConfigs),
        "artifact_spec_type" => FilterOptions.GetArtifactSpecTypeFilterOptions(),
        _ => [],
    };
}
