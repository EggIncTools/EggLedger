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

    public const string MennoMeasure = "menno_value";
    public const string RatioMeasure = "menno_ratio";

    public static HeatView? Heat(string compareMode) => compareMode switch {
        Ratio => new HeatView(Scale: HeatScale.Ratio),
        DualValue => new HeatView(ColorBy: RatioMeasure, SecondaryBy: MennoMeasure),
        _ => null,
    };

    public static ReportDefinition WithMode(ReportDefinition def, bool enabled, string compareMode) {
        ArgumentNullException.ThrowIfNull(def);
        var options = new Dictionary<string, string>(def.View.Options, StringComparer.Ordinal);
        if (enabled) {
            options[LedgerReportKeys.MennoOption] = "true";
            options[LedgerReportKeys.MennoCompareOption] = compareMode;
        } else {
            options.Remove(LedgerReportKeys.MennoOption);
            options.Remove(LedgerReportKeys.MennoCompareOption);
        }
        return def with { View = def.View with { Options = options, Heat = enabled ? Heat(compareMode) : null } };
    }

    public static bool ShouldRender(ReportDefinition def, ReportMatrix? user, ReportMatrix? menno) {
        return Enabled(def) && def.View.Chart == ReportCharts.Heatmap && user is not null && menno is not null;
    }

    public static ReportCube Combine(ReportCube user, string measureKey, ReportMatrix userMatrix, ReportMatrix menno, string compareMode) {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(userMatrix);
        ArgumentNullException.ThrowIfNull(menno);
        var measureIndex = user.MeasureIndex(measureKey);
        if (measureIndex < 0 || user.Dimensions.Count < 2) return user;

        var ratios = RatioCells(userMatrix, menno);
        var rowIndex = Positions(userMatrix.RowKeys);
        var colIndex = Positions(userMatrix.ColKeys);
        var mennoRowIndex = Positions(menno.RowKeys);
        var mennoColIndex = Positions(menno.ColKeys);
        var measure = user.Measures[measureIndex];
        var ratioFirst = compareMode == Ratio;

        List<CubeRow> rows = [];
        foreach (var row in user.Rows) {
            var key = (row.Keys[0], row.Keys[1]);
            double? ratio = rowIndex.TryGetValue(key.Item1, out var r) && colIndex.TryGetValue(key.Item2, out var c) ? ratios[r][c] : null;
            double? mennoValue = mennoRowIndex.TryGetValue(key.Item1, out var mr) && mennoColIndex.TryGetValue(key.Item2, out var mc) ? menno.Cells[mr][mc] : null;
            var value = row.Values[measureIndex];
            IReadOnlyList<double?> values = ratioFirst ? [ratio, value, mennoValue] : [value, mennoValue, ratio];
            rows.Add(new CubeRow(row.Keys, values, row.Samples));
        }

        var mennoRequest = new MeasureRequest(MennoMeasure, Aggregation.Sum);
        var ratioRequest = new MeasureRequest(RatioMeasure, Aggregation.Sum);
        IReadOnlyList<MeasureRequest> measures = ratioFirst ? [ratioRequest, measure, mennoRequest] : [measure, mennoRequest, ratioRequest];
        return user with { Measures = measures, Rows = rows };
    }

    public static ReportCube Community(ReportCube user, ReportMatrix menno) {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(menno);
        List<CubeRow> rows = [];
        for (var r = 0; r < menno.RowKeys.Count; r++) {
            for (var c = 0; c < menno.ColKeys.Count; c++) {
                if (menno.Cells[r][c] is not { } v) continue;
                rows.Add(new CubeRow([menno.RowKeys[r], menno.ColKeys[c]], [v], 0));
            }
        }
        return user with { Measures = [new MeasureRequest(MennoMeasure, Aggregation.Sum)], Rows = rows };
    }

    private static Dictionary<string, int> Positions(IReadOnlyList<string> keys) {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < keys.Count; i++) map.TryAdd(keys[i], i);
        return map;
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
