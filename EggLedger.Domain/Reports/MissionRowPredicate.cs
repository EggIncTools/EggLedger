using EggIdentity.Filters;

namespace EggLedger.Domain.Reports;

internal sealed class MissionRowPredicate(
    ReportDefinition def,
    ILookup<(string Player, string Mission), ArtifactDropRowData> dropsByMission) {

    private static readonly FilterEvaluation MissionPass = new(LedgerFilters.MissionScope, Strict: false);
    private static readonly FilterEvaluation ArtifactPass = new(LedgerFilters.ArtifactScope, Strict: false);

    public bool PassesFilters(MissionRowData m) =>
        FilterEvaluator.Evaluate(def.Filters, LedgerFilters.Fields, key => MissionValue(key, m), MissionPass).Matched;

    public bool PassesArtifactFilters(ArtifactDropRowData d) =>
        FilterEvaluator.Evaluate(def.Filters, LedgerFilters.Fields, key => ArtifactValue(key, d), ArtifactPass).Matched;

    private FilterSubjectValue MissionValue(string key, MissionRowData m) => key switch {
        "dubcap" => FilterSubjectValue.Of(m.IsDubCap),
        "buggedcap" => FilterSubjectValue.Of(m.IsBuggedCap),
        "drops" => FilterSubjectValue.Of(dropsByMission[(m.PlayerId, m.MissionId)]
            .Select(d => LedgerFilters.DropKey(d.ArtifactId, d.Level, d.Rarity))),
        "launchDT" => FilterSubjectValue.Of(DateTimeOffset.FromUnixTimeSeconds(m.StartTimestamp)),
        "returnDT" => FilterSubjectValue.Of(DateTimeOffset.FromUnixTimeSeconds(m.ReturnTimestamp)),
        "ship" => FilterSubjectValue.Of((double)m.Ship),
        "duration" => FilterSubjectValue.Of((double)m.DurationType),
        "level" => FilterSubjectValue.Of((double)m.Level),
        "target" => FilterSubjectValue.Of((double)m.Target),
        "type" => FilterSubjectValue.Of((double)m.MissionType),
        _ => FilterSubjectValue.Absent,
    };

    private static FilterSubjectValue ArtifactValue(string key, ArtifactDropRowData d) => key switch {
        "artifact_rarity" => FilterSubjectValue.Of((double)d.Rarity),
        "artifact_tier" => FilterSubjectValue.Of((double)d.Level),
        "artifact_name" => FilterSubjectValue.Of((double)d.ArtifactId),
        "artifact_quality" => FilterSubjectValue.Of(d.Quality),
        "artifact_spec_type" => FilterSubjectValue.Of(d.SpecType),
        _ => FilterSubjectValue.Absent,
    };
}
