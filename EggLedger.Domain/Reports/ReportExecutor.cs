using System.Globalization;
using EggLedger.Domain.Util;

namespace EggLedger.Domain.Reports;

public sealed class ReportExecutor(IMissionDb db, IWeightData weights) {
    private readonly IMissionDb _db = db ?? throw new ArgumentNullException(nameof(db));
    private readonly IWeightData _weights = weights ?? throw new ArgumentNullException(nameof(weights));

    public ReportResult ExecuteReport(ReportDefinition def) {
        var (whereClause, filterArgs) = QueryBuilder.BuildWhereClause(def.Filters);
        var baseWhere = "m.player_id = ?";
        if (whereClause != "") {
            baseWhere += " AND " + whereClause;
        }
        List<object?> args = [def.AccountId];
        args.AddRange(filterArgs);
        var baseArgs = new List<object?>(args);

        if (def.FamilyWeight != "") {
            var ids = _weights.FamilyAfxIds(def.FamilyWeight);
            var (fwClause, fwArgs) = QueryBuilder.FamilyWeightClause(ids);
            if (fwClause != "") {
                if (def.SecondaryGroupBy != "" && def.Mode == "time_series") {
                    return ExecuteWeightedTimePivot(def, baseWhere, args, fwClause, fwArgs);
                }
                if (def.SecondaryGroupBy != "") {
                    return ExecuteWeightedPivot(def, baseWhere, args, baseArgs, fwClause, fwArgs);
                }
                if (def.Mode == "time_series") {
                    return ExecuteWeightedTimeSeries(def, baseWhere, args, fwClause, fwArgs);
                }
                return ExecuteWeightedAggregate(def, baseWhere, args, baseArgs, fwClause, fwArgs);
            }
        }

        if (def.SecondaryGroupBy != "" && def.Mode == "time_series") {
            return ExecuteTimePivotReport(def, baseWhere, args);
        }
        if (def.SecondaryGroupBy != "") {
            return ExecutePivotReport(def, baseWhere, args, baseArgs);
        }

        return ExecuteSimpleReport(def, baseWhere, args, baseArgs);
    }

    private ReportResult ExecuteSimpleReport(ReportDefinition def, string baseWhere, List<object?> args, List<object?> baseArgs) {
        string query;
        (query, args) = def.Mode switch {
            "aggregate" => QueryBuilder.BuildAggregateQuery(def, baseWhere, args),
            "time_series" => QueryBuilder.BuildTimeSeriesQuery(def, baseWhere, args),
            _ => throw new InvalidOperationException($"unknown mode \"{def.Mode}\""),
        };
        var rows = _db.Query(query, args);

        if (def.Subject == "fuel_eggs" && def.Mode == "aggregate") {
            var fuelLabels = new List<string>();
            var fuelValues = new List<double>();
            foreach (var row in rows) {
                fuelLabels.Add(Labels.FormatLabel(def.GroupBy, AsString(row[0])));
                fuelValues.Add(AsDouble(row[1]));
            }
            return new ReportResult { Labels = fuelLabels, FloatValues = fuelValues, IsFloat = true, Weight = def.Weight };
        }

        var rawLabels = new List<string>();
        var labels = new List<string>();
        var values = new List<long>();
        foreach (var row in rows) {
            var rawLabel = AsString(row[0]);
            var count = AsLong(row[1]);
            rawLabels.Add(rawLabel);
            labels.Add(Labels.FormatLabel(def.GroupBy, rawLabel));
            values.Add(count);
        }

        if (def.Mode == "time_series" && rawLabels.Count > 0) {
            (rawLabels, values) = TimeFill.FillTimeSeriesGaps(def.TimeBucket, def.CustomBucketUnit, rawLabels, values);
            labels = new List<string>(rawLabels.Count);
            foreach (var rl in rawLabels) {
                labels.Add(Labels.FormatLabel(def.GroupBy, rl));
            }
        }

        if (def.NormalizeBy != "" && def.NormalizeBy != ReportDefaults.NormalizeNone && def.Mode == "aggregate") {
            var groupCol = QueryBuilder.GroupByColumn(def.GroupBy);
            if (groupCol == "" || groupCol.StartsWith("d.", StringComparison.Ordinal)) {
                return new ReportResult { Labels = labels, Values = values, Weight = def.Weight };
            }
            var denomMap = Denom1D(groupCol, def.NormalizeBy, baseWhere, baseArgs);

            var floatValues = new List<double>(values.Count);
            for (var i = 0; i < values.Count; i++) {
                floatValues.Add(0);
            }
            for (var i = 0; i < rawLabels.Count; i++) {
                denomMap.TryGetValue(rawLabels[i], out var denom);
                if (denom > 0) {
                    floatValues[i] = values[i] / denom;
                }
            }
            return new ReportResult {
                Labels = labels,
                FloatValues = floatValues,
                IsFloat = true,
                Weight = def.Weight,
            };
        }

        return new ReportResult { Labels = labels, Values = values, Weight = def.Weight };
    }

    private ReportResult ExecutePivotReport(ReportDefinition def, string baseWhere, List<object?> args, List<object?> baseArgs) {
        var (query, queryArgs) = QueryBuilder.BuildPivotQuery(def, baseWhere, args);

        var rows = _db.Query(query, queryArgs);

        var accum = new PivotAccum();
        foreach (var row in rows) {
            var rawRow = AsString(row[0]);
            var rawCol = AsString(row[1]);
            var count = AsLong(row[2]);
            var rowLabel = Labels.FormatLabel(def.GroupBy, rawRow);
            var colLabel = Labels.FormatLabel(def.SecondaryGroupBy, rawCol);
            accum.Add(rowLabel, rawRow, colLabel, rawCol, count);
        }

        var f = accum.Finalize(true, true, def.GroupBy, def.SecondaryGroupBy);
        var matrixValues = f.Matrix;

        var pctMode = def.NormalizeBy;
        if (pctMode is "row_pct" or "col_pct" or "global_pct") {
            Matrix.Apply2DPctNormalization(matrixValues, f.RowLabels.Count, f.ColLabels.Count, pctMode);
        } else if (pctMode is not "" and not ReportDefaults.NormalizeNone) {
            var col1 = QueryBuilder.GroupByColumn(def.GroupBy);
            var col2 = QueryBuilder.GroupByColumn(def.SecondaryGroupBy);
            if (col1 != "" && !col1.StartsWith("d.", StringComparison.Ordinal)
                && col2 != "" && !col2.StartsWith("d.", StringComparison.Ordinal)) {
                var denomMap = Denom2D(def, col1, col2, pctMode, baseWhere, baseArgs);
                var nC = f.ColLabels.Count;
                for (var r = 0; r < f.RowLabels.Count; r++) {
                    for (var c = 0; c < nC; c++) {
                        var d = Denom(denomMap, f.RowLabels[r], f.ColLabels[c]);
                        if (d > 0) {
                            matrixValues[(r * nC) + c] /= d;
                        }
                    }
                }
            }
        }

        return new ReportResult {
            RowLabels = f.RowLabels,
            ColLabels = f.ColLabels,
            MatrixValues = [.. matrixValues],
            Is2D = true,
            Weight = def.Weight,
            RawRowLabels = f.RawRowLabels,
            RawColLabels = f.RawColLabels,
        };
    }

    private ReportResult ExecuteTimePivotReport(ReportDefinition def, string baseWhere, List<object?> args) {
        var (query, queryArgs) = QueryBuilder.BuildTimePivotQuery(def, baseWhere, args);

        var rows = _db.Query(query, queryArgs);

        var accum = new PivotAccum();
        foreach (var row in rows) {
            var rawBucket = AsString(row[0]);
            var rawGrp = AsString(row[1]);
            var count = AsLong(row[2]);
            var grpLabel = Labels.FormatLabel(def.SecondaryGroupBy, rawGrp);
            accum.Add(rawBucket, rawBucket, grpLabel, rawGrp, count);
        }

        var f = accum.Finalize(false, true, def.GroupBy, def.SecondaryGroupBy);
        var bucketLabels = f.RowLabels;
        var matrixValues = f.Matrix;
        var nC = f.ColLabels.Count;

        if (bucketLabels.Count > 0) {
            (bucketLabels, matrixValues) = TimeFill.FillTimePivotGaps(def.TimeBucket, def.CustomBucketUnit, bucketLabels, nC, matrixValues);
        }
        var nR = bucketLabels.Count;

        var pctMode = def.NormalizeBy;
        if (pctMode is "row_pct" or "col_pct" or "global_pct") {
            Matrix.Apply2DPctNormalization(matrixValues, nR, nC, pctMode);
        }

        return new ReportResult {
            RowLabels = bucketLabels,
            ColLabels = f.ColLabels,
            MatrixValues = [.. matrixValues],
            Is2D = true,
            Weight = def.Weight,
            RawRowLabels = bucketLabels,
            RawColLabels = f.RawColLabels,
        };
    }

    private ReportResult ExecuteWeightedAggregate(ReportDefinition def, string baseWhere, List<object?> args, List<object?> baseArgs, string fwClause, IReadOnlyList<object?> fwArgs) {
        var (query, queryArgs) = QueryBuilder.BuildWeightedAggregateQuery(def, baseWhere, args, fwClause, fwArgs);
        var rows = _db.Query(query, queryArgs);

        var accum = new Dictionary<string, double>(StringComparer.Ordinal);
        var rawOrder = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in rows) {
            var rawLabel = AsString(row[0]);
            var artifactId = AsLong(row[1]);
            var level = AsLong(row[2]);
            var capWeight = AsDouble(row[3]);
            var w = _weights.CraftingWeight(artifactId, level);
            if (seen.Add(rawLabel)) {
                rawOrder.Add(rawLabel);
            }
            accum.TryGetValue(rawLabel, out var cur);
            accum[rawLabel] = cur + (capWeight * w);
        }

        var labels = new List<string>(rawOrder.Count);
        var floatValues = new List<double>(rawOrder.Count);
        foreach (var raw in rawOrder) {
            labels.Add(Labels.FormatLabel(def.GroupBy, raw));
            floatValues.Add(accum[raw]);
        }

        if (def.NormalizeBy != "" && def.NormalizeBy != ReportDefaults.NormalizeNone && def.Mode == "aggregate") {
            var groupCol = QueryBuilder.GroupByColumn(def.GroupBy);
            if (groupCol != "" && !groupCol.StartsWith("d.", StringComparison.Ordinal)) {
                var denomMap = Denom1D(groupCol, def.NormalizeBy, baseWhere, baseArgs);
                for (var i = 0; i < rawOrder.Count; i++) {
                    if (denomMap.TryGetValue(rawOrder[i], out var d) && d > 0) {
                        floatValues[i] /= d;
                    }
                }
            }
        }



        StableSortFloatDescending(floatValues);
        for (var i = 0; i < rawOrder.Count; i++) {
            labels[i] = Labels.FormatLabel(def.GroupBy, rawOrder[i]);
        }

        return new ReportResult {
            Labels = labels,
            FloatValues = floatValues,
            IsFloat = true,
            Weight = def.Weight,
        };
    }

    private ReportResult ExecuteWeightedPivot(ReportDefinition def, string baseWhere, List<object?> args, List<object?> baseArgs, string fwClause, IReadOnlyList<object?> fwArgs) {
        var (query, queryArgs) = QueryBuilder.BuildWeightedPivotQuery(def, baseWhere, args, fwClause, fwArgs);

        var rows = _db.Query(query, queryArgs);

        var accum = new PivotAccum();
        foreach (var row in rows) {
            var rawRow = AsString(row[0]);
            var rawCol = AsString(row[1]);
            var artifactId = AsLong(row[2]);
            var level = AsLong(row[3]);
            var capWeight = AsDouble(row[4]);
            var w = _weights.CraftingWeight(artifactId, level);
            var rowLabel = Labels.FormatLabel(def.GroupBy, rawRow);
            var colLabel = Labels.FormatLabel(def.SecondaryGroupBy, rawCol);
            accum.Add(rowLabel, rawRow, colLabel, rawCol, capWeight * w);
        }

        var f = accum.Finalize(true, true, def.GroupBy, def.SecondaryGroupBy);
        var matrixValues = f.Matrix;
        var rowLabels = f.RowLabels;
        var colLabels = f.ColLabels;


        var mcMap = BuildMissionCountMap(def, baseWhere, baseArgs);

        var missionCountMatrix = BuildMissionCountMatrix(mcMap, rowLabels, colLabels);

        var rawPerMissionValues = ApplyWeightedPivotNormalization(def, matrixValues, rowLabels, colLabels, mcMap, baseWhere, baseArgs);

        return new ReportResult {
            RowLabels = rowLabels,
            ColLabels = colLabels,
            MatrixValues = [.. matrixValues],
            Is2D = true,
            Weight = def.Weight,
            RawRowLabels = f.RawRowLabels,
            RawColLabels = f.RawColLabels,
            RawPerMissionValues = rawPerMissionValues,
            MissionCountMatrix = missionCountMatrix,
        };
    }

    private Dictionary<string, Dictionary<string, double>> BuildMissionCountMap(ReportDefinition def, string baseWhere, List<object?> baseArgs) {
        var col1mc = QueryBuilder.GroupByColumn(def.GroupBy);
        var col2mc = QueryBuilder.GroupByColumn(def.SecondaryGroupBy);
        var mcMap = new Dictionary<string, Dictionary<string, double>>(StringComparer.Ordinal);
        if (col1mc != "" && !col1mc.StartsWith("d.", StringComparison.Ordinal)
            && col2mc != "" && !col2mc.StartsWith("d.", StringComparison.Ordinal)) {
            var mcQuery = string.Format(
                CultureInfo.InvariantCulture,
                "SELECT CAST({0} AS TEXT), CAST({1} AS TEXT), COUNT(*) FROM mission m WHERE {2} GROUP BY {0}, {1}",
                col1mc, col2mc, baseWhere);
            var mcRows = _db.Query(mcQuery, baseArgs);
            foreach (var row in mcRows) {
                var rawKey1 = AsString(row[0]);
                var rawKey2 = AsString(row[1]);
                var cnt = AsDouble(row[2]);
                var k1 = Labels.FormatLabel(def.GroupBy, rawKey1);
                var k2 = Labels.FormatLabel(def.SecondaryGroupBy, rawKey2);
                if (!mcMap.TryGetValue(k1, out var inner)) {
                    inner = new Dictionary<string, double>(StringComparer.Ordinal);
                    mcMap[k1] = inner;
                }
                inner[k2] = cnt;
            }
        }
        return mcMap;
    }

    private static List<long> BuildMissionCountMatrix(Dictionary<string, Dictionary<string, double>> mcMap, List<string> rowLabels, List<string> colLabels) {
        var missionCountMatrix = new List<long>(rowLabels.Count * colLabels.Count);
        for (var r = 0; r < rowLabels.Count; r++) {
            for (var c = 0; c < colLabels.Count; c++) {
                missionCountMatrix.Add((long)Denom(mcMap, rowLabels[r], colLabels[c]));
            }
        }
        return missionCountMatrix;
    }


    private List<double>? ApplyWeightedPivotNormalization(ReportDefinition def, double[] matrixValues, List<string> rowLabels, List<string> colLabels, Dictionary<string, Dictionary<string, double>> mcMap, string baseWhere, List<object?> baseArgs) {
        var pctMode = def.NormalizeBy;
        List<double>? rawPerMissionValues = null;
        if (pctMode is "row_pct" or "col_pct" or "global_pct") {
            Matrix.Apply2DPctNormalization(matrixValues, rowLabels.Count, colLabels.Count, pctMode);
        } else if (pctMode is not "" and not ReportDefaults.NormalizeNone) {
            var col1 = QueryBuilder.GroupByColumn(def.GroupBy);
            var col2 = QueryBuilder.GroupByColumn(def.SecondaryGroupBy);
            if (col1 != "" && !col1.StartsWith("d.", StringComparison.Ordinal)
                && col2 != "" && !col2.StartsWith("d.", StringComparison.Ordinal)) {
                var nC = colLabels.Count;
                if (pctMode == "airtime") {
                    var rpm = new double[rowLabels.Count * nC];
                    Array.Copy(matrixValues, rpm, matrixValues.Length);
                    for (var r = 0; r < rowLabels.Count; r++) {
                        for (var c = 0; c < nC; c++) {
                            var d = Denom(mcMap, rowLabels[r], colLabels[c]);
                            if (d > 0) {
                                rpm[(r * nC) + c] /= d;
                            }
                        }
                    }
                    rawPerMissionValues = [.. rpm];
                }

                var denomMap = Denom2D(def, col1, col2, pctMode, baseWhere, baseArgs);
                for (var r = 0; r < rowLabels.Count; r++) {
                    for (var c = 0; c < nC; c++) {
                        var d = Denom(denomMap, rowLabels[r], colLabels[c]);
                        if (d > 0) {
                            matrixValues[(r * nC) + c] /= d;
                        }
                    }
                }
            }
        }
        return rawPerMissionValues;
    }

    private ReportResult ExecuteWeightedTimeSeries(ReportDefinition def, string baseWhere, List<object?> args, string fwClause, IReadOnlyList<object?> fwArgs) {
        var (query, queryArgs) = QueryBuilder.BuildWeightedTimeSeriesQuery(def, baseWhere, args, fwClause, fwArgs);
        var rows = _db.Query(query, queryArgs);

        var accum = new Dictionary<string, double>(StringComparer.Ordinal);
        var buckets = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in rows) {
            var rawBucket = AsString(row[0]);
            var artifactId = AsLong(row[1]);
            var level = AsLong(row[2]);
            var capWeight = AsDouble(row[3]);
            var w = _weights.CraftingWeight(artifactId, level);
            if (seen.Add(rawBucket)) {
                buckets.Add(rawBucket);
            }
            accum.TryGetValue(rawBucket, out var cur);
            accum[rawBucket] = cur + (capWeight * w);
        }

        var floatValues = new List<double>(buckets.Count);
        foreach (var b in buckets) {
            floatValues.Add(accum[b]);
        }

        (buckets, floatValues) = TimeFill.FillTimeSeriesGapsFloat(def.TimeBucket, def.CustomBucketUnit, buckets, floatValues);

        return new ReportResult {
            Labels = buckets,
            FloatValues = floatValues,
            IsFloat = true,
            Weight = def.Weight,
        };
    }

    private ReportResult ExecuteWeightedTimePivot(ReportDefinition def, string baseWhere, List<object?> args, string fwClause, IReadOnlyList<object?> fwArgs) {
        var (query, queryArgs) = QueryBuilder.BuildWeightedTimePivotQuery(def, baseWhere, args, fwClause, fwArgs);

        var rows = _db.Query(query, queryArgs);

        var accum = new PivotAccum();
        foreach (var row in rows) {
            var rawBucket = AsString(row[0]);
            var rawGrp = AsString(row[1]);
            var artifactId = AsLong(row[2]);
            var level = AsLong(row[3]);
            var capWeight = AsDouble(row[4]);
            var w = _weights.CraftingWeight(artifactId, level);
            var grpLabel = Labels.FormatLabel(def.SecondaryGroupBy, rawGrp);
            accum.Add(rawBucket, rawBucket, grpLabel, rawGrp, capWeight * w);
        }

        var f = accum.Finalize(false, true, def.GroupBy, def.SecondaryGroupBy);
        var bucketLabels = f.RowLabels;
        var matrixValues = f.Matrix;
        var nC = f.ColLabels.Count;

        if (bucketLabels.Count > 0) {
            (bucketLabels, matrixValues) = TimeFill.FillTimePivotGaps(def.TimeBucket, def.CustomBucketUnit, bucketLabels, nC, matrixValues);
        }
        var nR = bucketLabels.Count;

        var pctMode = def.NormalizeBy;
        if (pctMode is "row_pct" or "col_pct" or "global_pct") {
            Matrix.Apply2DPctNormalization(matrixValues, nR, nC, pctMode);
        }

        return new ReportResult {
            RowLabels = bucketLabels,
            ColLabels = f.ColLabels,
            MatrixValues = [.. matrixValues],
            Is2D = true,
            Weight = def.Weight,
            RawRowLabels = bucketLabels,
            RawColLabels = f.RawColLabels,
        };
    }

    private Dictionary<string, double> Denom1D(string groupCol, string mode, string baseWhere, IReadOnlyList<object?> baseArgs) {
        string denomQuery;
        if (mode == "airtime") {
            denomQuery = string.Format(
                CultureInfo.InvariantCulture,
                "SELECT CAST({0} AS TEXT), SUM(CAST(m.return_timestamp - m.start_timestamp AS REAL) / 3600.0) FROM mission m WHERE {1} GROUP BY {0}",
                groupCol, baseWhere);
        } else {
            denomQuery = string.Format(
                CultureInfo.InvariantCulture,
                "SELECT CAST({0} AS TEXT), COUNT(*) FROM mission m WHERE {1} GROUP BY {0}",
                groupCol, baseWhere);
        }
        var rows = _db.Query(denomQuery, baseArgs);
        var denomMap = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var row in rows) {
            denomMap[AsString(row[0])] = AsDouble(row[1]);
        }
        return denomMap;
    }

    private Dictionary<string, Dictionary<string, double>> Denom2D(ReportDefinition def, string col1, string col2, string mode, string baseWhere, IReadOnlyList<object?> baseArgs) {
        string denomQuery;
        if (mode == "airtime") {
            denomQuery = string.Format(
                CultureInfo.InvariantCulture,
                "SELECT CAST({0} AS TEXT), CAST({1} AS TEXT), SUM(CAST(m.return_timestamp - m.start_timestamp AS REAL) / 3600.0) FROM mission m WHERE {2} GROUP BY {0}, {1}",
                col1, col2, baseWhere);
        } else {
            denomQuery = string.Format(
                CultureInfo.InvariantCulture,
                "SELECT CAST({0} AS TEXT), CAST({1} AS TEXT), COUNT(*) FROM mission m WHERE {2} GROUP BY {0}, {1}",
                col1, col2, baseWhere);
        }
        var rows = _db.Query(denomQuery, baseArgs);
        var denomMap = new Dictionary<string, Dictionary<string, double>>(StringComparer.Ordinal);
        foreach (var row in rows) {
            var rawKey1 = AsString(row[0]);
            var rawKey2 = AsString(row[1]);
            var denom = AsDouble(row[2]);
            var k1 = Labels.FormatLabel(def.GroupBy, rawKey1);
            var k2 = Labels.FormatLabel(def.SecondaryGroupBy, rawKey2);
            if (!denomMap.TryGetValue(k1, out var inner)) {
                inner = new Dictionary<string, double>(StringComparer.Ordinal);
                denomMap[k1] = inner;
            }
            inner[k2] = denom;
        }
        return denomMap;
    }

    private static double Denom(Dictionary<string, Dictionary<string, double>> m, string k1, string k2) =>
        m.TryGetValue(k1, out var inner) && inner.TryGetValue(k2, out var v) ? v : 0;



    private static void StableSortFloatDescending(List<double> values) {
        var ordered = values
            .Select((v, i) => (v, i))
            .OrderByDescending(x => x.v)
            .ThenBy(x => x.i)
            .Select(x => x.v)
            .ToList();
        for (var i = 0; i < values.Count; i++) {
            values[i] = ordered[i];
        }
    }

    private static string AsString(object? v) => v switch {
        null => "",
        string s => s,
        _ => Convert.ToString(v, CultureInfo.InvariantCulture) ?? "",
    };

    private static long AsLong(object? v) => v switch {
        null => 0,
        long l => l,
        int i => i,
        double d => (long)d,
        string s => long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) ? r : 0,
        _ => Convert.ToInt64(v, CultureInfo.InvariantCulture),
    };

    private static double AsDouble(object? v) => v switch {
        null => 0,
        double d => d,
        long l => l,
        int i => i,
        string s => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var r) ? r : 0,
        _ => Convert.ToDouble(v, CultureInfo.InvariantCulture),
    };
}
