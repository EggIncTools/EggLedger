using EggIdentity.Reports;

namespace EggLedger.Domain.Reports;

public static class MennoComparison {
    public const string SideBySide = "side_by_side";
    public const string Ratio = "ratio";
    public const string DualValue = "dual_value";

    public static bool Enabled(ReportDefinition def) {
        ArgumentNullException.ThrowIfNull(def);
        if (!def.View.Options.TryGetValue(LedgerReportKeys.MennoOption, out var menno) || menno != "true") return false;
        if (def.Query.Source != LedgerReportKeys.Artifacts) return false;
        if (def.Query.GroupBy.Count != 2) return false;
        return def.Query.GroupBy.All(g => g.Bucket is null && ComparableDimension(g.Dimension));
    }

    public static bool ComparableDimension(string key) => key switch {
        LedgerReportKeys.Ship or LedgerReportKeys.Duration or LedgerReportKeys.Level or LedgerReportKeys.Target
            or LedgerReportKeys.ArtifactName or LedgerReportKeys.Rarity or LedgerReportKeys.Tier => true,
        _ => false,
    };

    public static string CompareMode(ReportDefinition def) {
        ArgumentNullException.ThrowIfNull(def);
        return def.View.Options.TryGetValue(LedgerReportKeys.MennoCompareOption, out var mode) ? mode : SideBySide;
    }

    public static bool ShouldRender(ReportDefinition def, ReportMatrix? user, ReportMatrix? menno) {
        return Enabled(def) && def.View.Chart == ReportCharts.Heatmap && user is not null && menno is not null;
    }

    public static IReadOnlyList<IReadOnlyList<double?>> RatioCells(ReportMatrix user, ReportMatrix menno) {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(menno);
        var lookup = new Dictionary<(string Row, string Col), double?>();
        for (var r = 0; r < menno.RowKeys.Count; r++) {
            for (var c = 0; c < menno.ColKeys.Count; c++) {
                lookup[(menno.RowKeys[r], menno.ColKeys[c])] = menno.Cells[r][c];
            }
        }
        var result = new List<IReadOnlyList<double?>>();
        for (var r = 0; r < user.RowKeys.Count; r++) {
            var row = new List<double?>();
            for (var c = 0; c < user.ColKeys.Count; c++) {
                var userVal = user.Cells[r][c];
                var mennoVal = lookup.TryGetValue((user.RowKeys[r], user.ColKeys[c]), out var v) ? v : null;
                row.Add(userVal is null || mennoVal is null or 0 ? null : userVal.Value / mennoVal.Value);
            }
            result.Add(row);
        }
        return result;
    }
}
