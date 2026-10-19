using System.Globalization;
using EggIdentity.Filters;
using EggIdentity.Reports;
using EggLedger.Domain.Ei;
using EggLedger.Domain.Eiafx;
using EggLedger.Domain.LedgerData;
using EggLedger.Domain.MissionPacking;
using Ei;

namespace EggLedger.Domain.Reports;

public sealed record LedgerMissionRow(MissionRowData Mission, IReadOnlyList<ArtifactDropRowData> Drops);

public sealed record LedgerArtifactRow(
    MissionRowData Mission,
    ArtifactDropRowData Drop,
    IReadOnlyList<ArtifactDropRowData> MissionDrops,
    double CraftingWeight,
    IReadOnlySet<string> Families);

public sealed record LedgerFuelRow(MissionRowData Mission, FuelRowData Fuel, IReadOnlyList<ArtifactDropRowData> MissionDrops);

public static class LedgerReportSources {
    private static readonly string[] RarityNames = ["Common", "Rare", "Epic", "Legendary"];
    private static readonly string[] TierNames = ["T1", "T2", "T3", "T4"];
    private static readonly IReadOnlySet<string> EmptyFamilies = new HashSet<string>();

    private static readonly FilterFieldDef FamilyField =
        new(LedgerReportKeys.Family, "Family", FilterValueKind.Select, FilterOps.Equality) { Scope = LedgerFilters.ArtifactScope };

    private static readonly FilterFieldDef MissionIdField =
        new(LedgerReportKeys.MissionId, "Mission", FilterValueKind.Text, FilterOps.Equality) { Scope = LedgerFilters.MissionScope };

    public static ReportSource<LedgerMissionRow> Missions { get; } = new(
        LedgerReportKeys.Missions,
        "Missions",
        [.. MissionDimensions(), new ReportDimension(MissionIdField)],
        [AirtimeMeasure(null), LaunchesMeasure(null)],
        MissionsValue);

    public static ReportSource<LedgerArtifactRow> Artifacts { get; } = new(
        LedgerReportKeys.Artifacts,
        "Artifacts",
        [.. MissionDimensions(), .. ArtifactDimensions(), new ReportDimension(FamilyField), new ReportDimension(MissionIdField)],
        [AirtimeMeasure(LedgerReportKeys.MissionId), LaunchesMeasure(LedgerReportKeys.MissionId), CraftingWeightMeasure()],
        ArtifactsValue) {
        SampleKey = LedgerReportKeys.MissionId,
    };

    public static ReportSource<LedgerFuelRow> Fuel { get; } = new(
        LedgerReportKeys.Fuel,
        "Fuel eggs",
        [.. MissionDimensions(), new ReportDimension(MissionIdField)],
        [AirtimeMeasure(LedgerReportKeys.MissionId), LaunchesMeasure(LedgerReportKeys.MissionId), FuelAmountMeasure()],
        FuelValue) {
        SampleKey = LedgerReportKeys.MissionId,
    };

    public static IReadOnlyList<IReportSource> All { get; } = [Missions, Artifacts, Fuel];

    public static ReportEngineRegistry Registry(Func<CancellationToken, ValueTask<LedgerReportData>> data, IWeightData weights, TimeProvider clock) {
        var registry = new ReportEngineRegistry();
        registry.Add(LedgerReportKeys.Missions, new InMemoryReportEngine<LedgerMissionRow>(Missions, ct => LoadMissions(data, ct), clock));
        registry.Add(LedgerReportKeys.Artifacts, new InMemoryReportEngine<LedgerArtifactRow>(Artifacts, ct => LoadArtifacts(data, weights, ct), clock));
        registry.Add(LedgerReportKeys.Fuel, new InMemoryReportEngine<LedgerFuelRow>(Fuel, ct => LoadFuel(data, ct), clock));
        return registry;
    }

    private static async ValueTask<IReadOnlyList<LedgerMissionRow>> LoadMissions(Func<CancellationToken, ValueTask<LedgerReportData>> data, CancellationToken ct) =>
        MissionRows(await data(ct).ConfigureAwait(false));

    private static async ValueTask<IReadOnlyList<LedgerArtifactRow>> LoadArtifacts(
        Func<CancellationToken, ValueTask<LedgerReportData>> data, IWeightData weights, CancellationToken ct) =>
        ArtifactRows(await data(ct).ConfigureAwait(false), weights);

    private static async ValueTask<IReadOnlyList<LedgerFuelRow>> LoadFuel(Func<CancellationToken, ValueTask<LedgerReportData>> data, CancellationToken ct) =>
        FuelRows(await data(ct).ConfigureAwait(false));

    public static IReadOnlyList<LedgerMissionRow> MissionRows(LedgerReportData report) {
        ArgumentNullException.ThrowIfNull(report);
        var dropsByMission = report.Drops.ToLookup(d => (d.PlayerId, d.MissionId));
        return [.. report.Missions.Select(m => new LedgerMissionRow(m, [.. dropsByMission[(m.PlayerId, m.MissionId)]]))];
    }

    public static IReadOnlyList<LedgerArtifactRow> ArtifactRows(LedgerReportData report, IWeightData weights) {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(weights);
        var missionsByKey = report.Missions.ToDictionary(m => (m.PlayerId, m.MissionId));
        var dropsByMission = report.Drops.ToLookup(d => (d.PlayerId, d.MissionId));
        var familiesByArtifact = new Dictionary<long, HashSet<string>>();
        foreach (var familyId in EiafxData.FamilyAfxIds.Keys) {
            foreach (var artifactId in weights.FamilyAfxIds(familyId)) {
                if (!familiesByArtifact.TryGetValue(artifactId, out var set)) {
                    set = [];
                    familiesByArtifact[artifactId] = set;
                }
                set.Add(familyId);
            }
        }
        List<LedgerArtifactRow> rows = [];
        foreach (var drop in report.Drops) {
            if (drop.DropIndex < 0) continue;
            if (!missionsByKey.TryGetValue((drop.PlayerId, drop.MissionId), out var mission)) continue;
            var missionDrops = (IReadOnlyList<ArtifactDropRowData>)[.. dropsByMission[(drop.PlayerId, drop.MissionId)]];
            var capWeight = mission.NominalCapacity > 0 && mission.Capacity > 0
                ? (double)mission.NominalCapacity / mission.Capacity
                : 1.0;
            var craftingWeight = capWeight * weights.CraftingWeight(drop.ArtifactId, drop.Level);
            var families = familiesByArtifact.TryGetValue(drop.ArtifactId, out var set) ? set : EmptyFamilies;
            rows.Add(new LedgerArtifactRow(mission, drop, missionDrops, craftingWeight, families));
        }
        return rows;
    }

    public static IReadOnlyList<LedgerFuelRow> FuelRows(LedgerReportData report) {
        ArgumentNullException.ThrowIfNull(report);
        var fuelByMission = report.Fuel.ToLookup(f => (f.PlayerId, f.MissionId));
        var dropsByMission = report.Drops.ToLookup(d => (d.PlayerId, d.MissionId));
        List<LedgerFuelRow> rows = [];
        foreach (var mission in report.Missions) {
            var key = (mission.PlayerId, mission.MissionId);
            IReadOnlyList<ArtifactDropRowData> drops = [.. dropsByMission[key]];
            var recorded = fuelByMission[key].ToList();
            IEnumerable<FuelRowData> fuels = recorded.Count > 0
                ? recorded
                : ShipFuelCosts.For(mission.Ship, mission.DurationType).Select(f => new FuelRowData {
                    PlayerId = mission.PlayerId,
                    MissionId = mission.MissionId,
                    EggId = f.EggId,
                    Amount = f.Amount,
                });
            rows.AddRange(fuels.Select(f => new LedgerFuelRow(mission, f, drops)));
        }
        return rows;
    }

    private static FilterSubjectValue MissionsValue(LedgerMissionRow row, string key) => key switch {
        LedgerReportKeys.MissionId => FilterSubjectValue.Of(row.Mission.MissionId),
        LedgerReportKeys.Airtime => FilterSubjectValue.Of(Airtime(row.Mission)),
        LedgerReportKeys.Launches => FilterSubjectValue.Of(1.0),
        _ => MissionValue(key, row.Mission, row.Drops),
    };

    private static FilterSubjectValue ArtifactsValue(LedgerArtifactRow row, string key) => key switch {
        LedgerReportKeys.MissionId => FilterSubjectValue.Of(row.Mission.MissionId),
        LedgerReportKeys.Airtime => FilterSubjectValue.Of(Airtime(row.Mission)),
        LedgerReportKeys.Launches => FilterSubjectValue.Of(1.0),
        LedgerReportKeys.CraftingWeight => FilterSubjectValue.Of(row.CraftingWeight),
        LedgerReportKeys.Family => FilterSubjectValue.Of(row.Families),
        _ when LedgerFilters.IsArtifactField(key) => ArtifactValue(key, row.Drop),
        _ => MissionValue(key, row.Mission, row.MissionDrops),
    };

    private static FilterSubjectValue FuelValue(LedgerFuelRow row, string key) => key switch {
        LedgerReportKeys.MissionId => FilterSubjectValue.Of(row.Mission.MissionId),
        LedgerReportKeys.Airtime => FilterSubjectValue.Of(Airtime(row.Mission)),
        LedgerReportKeys.Launches => FilterSubjectValue.Of(1.0),
        LedgerReportKeys.FuelAmount => FilterSubjectValue.Of(row.Fuel.Amount),
        _ => MissionValue(key, row.Mission, row.MissionDrops),
    };

    private static double Airtime(MissionRowData m) => (m.ReturnTimestamp - m.StartTimestamp) / 3600.0;

    private static FilterSubjectValue MissionValue(string key, MissionRowData m, IReadOnlyList<ArtifactDropRowData> drops) => key switch {
        LedgerReportKeys.DubCap => FilterSubjectValue.Of(m.IsDubCap),
        LedgerReportKeys.BuggedCap => FilterSubjectValue.Of(m.IsBuggedCap),
        LedgerReportKeys.Drops => FilterSubjectValue.Of(drops.Select(d => LedgerFilters.DropKey(d.ArtifactId, d.Level, d.Rarity))),
        LedgerReportKeys.Launched => FilterSubjectValue.Of(DateTimeOffset.FromUnixTimeSeconds(m.StartTimestamp)),
        LedgerReportKeys.Returned => FilterSubjectValue.Of(DateTimeOffset.FromUnixTimeSeconds(m.ReturnTimestamp)),
        LedgerReportKeys.Ship => FilterSubjectValue.Of((double)m.Ship),
        LedgerReportKeys.Duration => FilterSubjectValue.Of((double)m.DurationType),
        LedgerReportKeys.Level => FilterSubjectValue.Of((double)m.Level),
        LedgerReportKeys.Target => FilterSubjectValue.Of((double)m.Target),
        LedgerReportKeys.MissionType => FilterSubjectValue.Of((double)m.MissionType),
        _ => FilterSubjectValue.Absent,
    };

    private static FilterSubjectValue ArtifactValue(string key, ArtifactDropRowData d) => key switch {
        LedgerReportKeys.Rarity => FilterSubjectValue.Of((double)d.Rarity),
        LedgerReportKeys.Tier => FilterSubjectValue.Of((double)d.Level),
        LedgerReportKeys.ArtifactName => FilterSubjectValue.Of((double)d.ArtifactId),
        LedgerReportKeys.Quality => FilterSubjectValue.Of(d.Quality),
        LedgerReportKeys.SpecType => FilterSubjectValue.Of(d.SpecType),
        _ => FilterSubjectValue.Absent,
    };

    private static IReadOnlyList<ReportDimension> MissionDimensions() => [
        Dim("ship", ShipOptions(), DimensionOrder.OptionOrder),
        Dim("duration", DurationOptions(), DimensionOrder.OptionOrder),
        Dim("level", LevelOptions(), DimensionOrder.OptionOrder),
        Dim("target", TargetOptions(), DimensionOrder.Natural),
        Dim("type", TypeOptions(), DimensionOrder.Natural),
        new(Field("launchDT")),
        new(Field("returnDT")),
        new(Field("dubcap")),
        new(Field("buggedcap")),
        new(Field("drops")),
    ];

    private static IReadOnlyList<ReportDimension> ArtifactDimensions() => [
        Dim("artifact_name", ArtifactNameOptions(), DimensionOrder.Natural),
        Dim("artifact_rarity", RarityOptions(), DimensionOrder.OptionOrder),
        Dim("artifact_tier", TierOptions(), DimensionOrder.OptionOrder),
        new(Field("artifact_spec_type")),
        new(Field("artifact_quality")),
    ];

    private static FilterFieldDef Field(string key) => LedgerFilters.Find(key) ?? throw new InvalidOperationException(key);

    private static ReportDimension Dim(string key, IReadOnlyList<FilterOption> options, DimensionOrder order) =>
        new(Field(key) with { Options = options }) { Order = order };

    private static ReportMeasure AirtimeMeasure(string? distinctBy) => new(LedgerReportKeys.Airtime, "Flight hours") {
        Aggregations = [Aggregation.Sum],
        Default = Aggregation.Sum,
        Format = ValueFormat.Decimal(1),
        Scopes = [LedgerFilters.MissionScope],
        DistinctBy = distinctBy,
    };

    private static ReportMeasure LaunchesMeasure(string? distinctBy) => new(LedgerReportKeys.Launches, "Launches") {
        Aggregations = [Aggregation.Sum],
        Default = Aggregation.Sum,
        Format = ValueFormat.Integer,
        Scopes = [LedgerFilters.MissionScope],
        DistinctBy = distinctBy,
    };

    private static ReportMeasure CraftingWeightMeasure() => new(LedgerReportKeys.CraftingWeight, "Crafting weight") {
        Aggregations = [Aggregation.Sum, Aggregation.Avg],
        Default = Aggregation.Sum,
        Format = ValueFormat.Decimal(2),
    };

    private static ReportMeasure FuelAmountMeasure() => new(LedgerReportKeys.FuelAmount, "Eggs used") {
        Aggregations = [Aggregation.Sum],
        Default = Aggregation.Sum,
        Format = ValueFormat.Decimal(0),
    };

    private static string NumberKey(long v) => v.ToString(CultureInfo.InvariantCulture);

    private static IReadOnlyList<FilterOption> ShipOptions() =>
        [.. Enum.GetValues<MissionInfo.Spaceship>().Select(v => new FilterOption(Value: NumberKey((long)v), Label: v.Name()))];

    private static IReadOnlyList<FilterOption> DurationOptions() =>
        [.. Enum.GetValues<MissionInfo.DurationType>().Select(v => new FilterOption(Value: NumberKey((long)v), Label: v.Display()))];

    private static IReadOnlyList<FilterOption> TypeOptions() =>
        [.. Enum.GetValues<MissionInfo.MissionType>().Select(v => new FilterOption(Value: NumberKey((long)v), Label: v.Display()))];

    private static IReadOnlyList<FilterOption> LevelOptions() =>
        [.. Enumerable.Range(0, 11).Select(i => new FilterOption(Value: NumberKey(i), Label: $"Level {i}"))];

    private static IReadOnlyList<FilterOption> RarityOptions() =>
        [.. Enumerable.Range(0, RarityNames.Length).Select(i => new FilterOption(Value: NumberKey(i), Label: RarityNames[i]))];

    private static IReadOnlyList<FilterOption> TierOptions() =>
        [.. Enumerable.Range(0, TierNames.Length).Select(i => new FilterOption(Value: NumberKey(i), Label: TierNames[i]))];

    private static IReadOnlyList<FilterOption> TargetOptions() => [
        new FilterOption(Value: NumberKey(-1), Label: "None (Pre 1.27)"),
        new FilterOption(Value: NumberKey(0), Label: "Untargeted"),
        .. Enum.GetValues<ArtifactSpec.Name>().Select(v => new FilterOption(Value: NumberKey((long)v), Label: ArtifactDisplayName((int)v))),
    ];

    private static IReadOnlyList<FilterOption> ArtifactNameOptions() =>
        [.. Enum.GetValues<ArtifactSpec.Name>().Select(v => new FilterOption(Value: NumberKey((long)v), Label: ArtifactDisplayName((int)v)))];

    private static LedgerDisplayData Config => LedgerData.LedgerData.Config;

    private static string ArtifactDisplayName(int v) {
        var protoName = EnumNames.ProtoName((ArtifactSpec.Name)v);
        foreach (var t in Config.ArtifactTargets) {
            if (t.Name == protoName) return t.DisplayName;
        }
        return ((ArtifactSpec.Name)v).CasedName();
    }
}
