namespace EggLedger.Domain.Reports;

public static class MennoComparison {
    public const string SideBySide = "side_by_side";
    public const string Ratio = "ratio";
    public const string DualValue = "dual_value";

    public static bool ShouldRender(ReportDefinition def, ReportResult? userResult, ReportResult? mennoResult) {
        if (def is null || !def.MennoEnabled) {
            return false;
        }
        if (def.DisplayMode != "heatmap") {
            return false;
        }
        if (userResult is null || !userResult.Is2D || mennoResult is null) {
            return false;
        }
        return true;
    }

    public static ReportResult MennoAirtimeResult(ReportDefinition def, ReportResult mennoResult) {
        if (def.NormalizeBy != "airtime") {
            return mennoResult;
        }
        var avm = mennoResult.AirtimeMatrixValues;
        if (avm is null || avm.Count == 0) {
            return mennoResult;
        }
        return Clone(mennoResult, matrixValues: [.. avm]);
    }

    public static ReportResult UserPerMissionResult(ReportResult userResult) {
        var rpm = userResult.RawPerMissionValues;
        if (rpm is null || rpm.Count == 0) {
            return userResult;
        }
        return Clone(userResult, matrixValues: [.. rpm]);
    }

    public static IReadOnlyList<double?> RatioMatrix(ReportResult userResult, ReportResult mennoResult) {
        var numerator = userResult.RawPerMissionValues is { Count: > 0 } rpm
            ? rpm
            : userResult.MatrixValues;
        var menno = mennoResult.MatrixValues;
        return [.. numerator.Select((n, i) => {
            double mennoVal = i < menno.Count ? menno[i] : 0;
            return mennoVal == 0 ? (double?)null : n / mennoVal;
        })];
    }

    public static ReportResult RatioResult(ReportResult userResult, ReportResult mennoResult) {
        var ratio = RatioMatrix(userResult, mennoResult);
        List<double> vals = [.. ratio.Select(v => v ?? 0)];
        var clone = Clone(userResult, matrixValues: vals);
        clone.IsFloat = true;
        return clone;
    }

    public static IReadOnlyList<double> RatioColorValues(ReportResult userResult, ReportResult mennoResult) {
        var ratio = RatioMatrix(userResult, mennoResult);
        return [.. ratio.Select(v => v ?? 0)];
    }

    private static ReportResult Clone(ReportResult src, List<double> matrixValues) => new() {
        Labels = src.Labels,
        Values = src.Values,
        FloatValues = src.FloatValues,
        IsFloat = src.IsFloat,
        Weight = src.Weight,
        RowLabels = src.RowLabels,
        ColLabels = src.ColLabels,
        MatrixValues = matrixValues,
        Is2D = src.Is2D,
        RawRowLabels = src.RawRowLabels,
        RawColLabels = src.RawColLabels,
        RawPerMissionValues = src.RawPerMissionValues,
        AirtimeMatrixValues = src.AirtimeMatrixValues,
        MissionCountMatrix = src.MissionCountMatrix,
    };
}
