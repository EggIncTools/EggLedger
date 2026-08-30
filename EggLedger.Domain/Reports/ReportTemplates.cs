using System.Text.Json;

namespace EggLedger.Domain.Reports;

public static class ReportTemplates {
    public const string TotalFuelByShip = "tmpl_total_fuel_by_ship";
    public const string ShipMix = "tmpl_ship_mix";
    public const string DurationMix = "tmpl_duration_mix";
    public const string RarityMix = "tmpl_rarity_mix";

    public static readonly IReadOnlyList<ReportDefinition> All = [
        new ReportDefinition {
            Id = TotalFuelByShip,
            Name = "Total Eggs Used for Fuel",
            Subject = "fuel_eggs",
            Mode = "aggregate",
            DisplayMode = "bar",
            ChartType = "bar",
            GroupBy = "ship_type",
            Weight = "LOW",
            Color = "#f59e0b",
            NormalizeBy = ReportDefaults.NormalizeNone,
            GridW = 2,
            GridH = 2,
        },
        new ReportDefinition {
            Id = ShipMix,
            Name = "Fleet Mix",
            Subject = "missions",
            Mode = "aggregate",
            DisplayMode = "bar",
            ChartType = "bar",
            GroupBy = "ship_type",
            Weight = "LOW",
            Color = "#6366f1",
            NormalizeBy = ReportDefaults.NormalizeNone,
            GridW = 2,
            GridH = 2,
        },
        new ReportDefinition {
            Id = DurationMix,
            Name = "Mission Duration Mix",
            Subject = "missions",
            Mode = "aggregate",
            DisplayMode = "bar",
            ChartType = "bar",
            GroupBy = "duration_type",
            Weight = "LOW",
            Color = "#22c55e",
            NormalizeBy = ReportDefaults.NormalizeNone,
            GridW = 2,
            GridH = 2,
        },
        new ReportDefinition {
            Id = RarityMix,
            Name = "Artifact Rarity Mix",
            Subject = "artifacts",
            Mode = "aggregate",
            DisplayMode = "bar",
            ChartType = "bar",
            GroupBy = "rarity",
            Weight = "MEDIUM",
            Color = "#a855f7",
            NormalizeBy = ReportDefaults.NormalizeNone,
            GridW = 2,
            GridH = 2,
        },
    ];

    public static ReportDefinition? Find(string id) {
        var match = All.FirstOrDefault(d => d.Id == id);
        return match is null ? null : JsonSerializer.Deserialize<ReportDefinition>(JsonSerializer.Serialize(match));
    }
}
