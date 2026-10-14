using EggIdentity.Reports;

namespace EggLedger.Domain.Reports;

public static class ReportTemplates {
    public const string TotalFuelByShip = "tmpl_total_fuel_by_ship";
    public const string ShipMix = "tmpl_ship_mix";
    public const string DurationMix = "tmpl_duration_mix";
    public const string RarityMix = "tmpl_rarity_mix";

    public static readonly IReadOnlyList<ReportDefinition> All = [
        new ReportDefinition(ReportCodec.CurrentVersion, TotalFuelByShip, "Total Eggs Used for Fuel",
            new ReportQuery(LedgerReportKeys.Fuel) {
                GroupBy = [new GroupKey(LedgerReportKeys.Ship)],
                Measures = [new MeasureRequest(LedgerReportKeys.FuelAmount, Aggregation.Sum)],
                Sort = [new SortKey(LedgerReportKeys.FuelAmount, Descending: true)],
            },
            new ReportView(ReportCharts.Bar) { Color = "#f59e0b" }),
        new ReportDefinition(ReportCodec.CurrentVersion, ShipMix, "Fleet Mix",
            new ReportQuery(LedgerReportKeys.Missions) {
                GroupBy = [new GroupKey(LedgerReportKeys.Ship)],
                Measures = [],
                Sort = [new SortKey(MeasureRequest.RowCountKey, Descending: true)],
            },
            new ReportView(ReportCharts.Bar) { Color = "#6366f1" }),
        new ReportDefinition(ReportCodec.CurrentVersion, DurationMix, "Mission Duration Mix",
            new ReportQuery(LedgerReportKeys.Missions) {
                GroupBy = [new GroupKey(LedgerReportKeys.Duration)],
                Measures = [],
                Sort = [new SortKey(MeasureRequest.RowCountKey, Descending: true)],
            },
            new ReportView(ReportCharts.Bar) { Color = "#22c55e" }),
        new ReportDefinition(ReportCodec.CurrentVersion, RarityMix, "Artifact Rarity Mix",
            new ReportQuery(LedgerReportKeys.Artifacts) {
                GroupBy = [new GroupKey(LedgerReportKeys.Rarity)],
                Measures = [],
                Sort = [new SortKey(MeasureRequest.RowCountKey, Descending: true)],
            },
            new ReportView(ReportCharts.Bar) { Color = "#a855f7" }),
    ];

    public static ReportDefinition? Find(string id) => All.FirstOrDefault(d => d.Id == id);

    public static ReportPlacement Placement(string id, int order) => new(id, 0, 0, 2, 2, order);
}
