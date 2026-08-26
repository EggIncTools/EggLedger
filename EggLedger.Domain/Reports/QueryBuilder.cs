using System.Globalization;




namespace EggLedger.Domain.Reports;

public static class QueryBuilder {
    private static bool IsInt(string s) =>
        long.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _);

    private static bool IsFloat(string s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out _);

    public static (string clause, List<object?> args) BuildWhereClause(ReportFilters filters) {
        var clauses = new List<string>();
        var args = new List<object?>();

        void AddCond(FilterCondition c) {
            var (clause, cargs) = ConditionToSql(c);
            if (clause == "") {
                return;
            }
            clauses.Add(clause);
            args.AddRange(cargs);
        }

        foreach (var c in filters.And) {
            AddCond(c);
        }

        foreach (var group in filters.Or) {
            var orParts = new List<string>();
            foreach (var c in group) {
                var (clause, cargs) = ConditionToSql(c);
                if (clause == "") {
                    continue;
                }
                orParts.Add(clause);
                args.AddRange(cargs);
            }
            if (orParts.Count > 0) {
                clauses.Add("(" + string.Join(" OR ", orParts) + ")");
            }
        }

        if (clauses.Count == 0) {
            return ("", []);
        }
        return (string.Join(" AND ", clauses), args);
    }

    private static readonly Dictionary<string, string> MissionFieldToColumn = new() {
        ["ship"] = "m.ship",
        ["duration"] = "m.duration_type",
        ["level"] = "m.level",
        ["target"] = "m.target",
        ["type"] = "m.mission_type",
        ["launchDT"] = "m.start_timestamp",
        ["returnDT"] = "m.return_timestamp",
    };

    private static readonly Dictionary<string, string> ArtifactFieldToColumn = new() {
        ["artifact_rarity"] = "d.rarity",
        ["artifact_spec_type"] = "d.spec_type",
        ["artifact_name"] = "d.artifact_id",
        ["artifact_tier"] = "d.level",
        ["artifact_quality"] = "d.quality",
    };

    public static (string clause, List<object?> args) ConditionToSql(FilterCondition c) {
        switch (c.TopLevel) {
            case "dubcap":
                return c.Op == "true"
                    ? ("m.is_dub_cap = 1", [])
                    : ("m.is_dub_cap = 0", []);
            case "buggedcap":
                return c.Op == "true"
                    ? ("m.is_bugged_cap = 1", [])
                    : ("m.is_bugged_cap = 0", []);
            case "drops":
                if (c.Op is not "c" and not "dnc") {
                    return ("", []);
                }
                if (c.Val == "") {
                    return ("", []);
                }
                var parts = c.Val.Split('_');

                string[] cols = ["artifact_id", "level", "rarity"];
                var preds = new List<string>();
                var qargs = new List<object?>();
                for (var i = 0; i < cols.Length; i++) {
                    if (i >= parts.Length || parts[i] == "%" || parts[i] == "") {
                        continue;
                    }
                    preds.Add("AND " + cols[i] + " = ?");
                    qargs.Add(parts[i]);
                }
                var inner = "SELECT 1 FROM artifact_drops WHERE mission_id = m.mission_id AND player_id = m.player_id";
                foreach (var p in preds) {
                    inner += " " + p;
                }
                var exists = "EXISTS (" + inner + ")";
                if (c.Op == "dnc") {
                    exists = "NOT " + exists;
                }
                return (exists, qargs);
        }

        if (c.TopLevel is "launchDT" or "returnDT") {
            var col = MissionFieldToColumn[c.TopLevel];
            return c.Op switch {
                ">" or "<" or ">=" or "<=" => ($"{col} {c.Op} strftime('%s', ?)", [c.Val]),
                "=" or "d=" => ($"{col} = strftime('%s', ?)", [c.Val]),
                _ => ("", []),
            };
        }

        if (MissionFieldToColumn.TryGetValue(c.TopLevel, out var mcol)) {
            switch (c.Op) {
                case "=":
                case "!=":
                case ">":
                case "<":
                case ">=":
                case "<=":


                    if (!IsInt(c.Val)) {
                        return ("", []);
                    }
                    return ($"{mcol} {c.Op} ?", [c.Val]);
            }
            return ("", []);
        }

        if (ArtifactFieldToColumn.TryGetValue(c.TopLevel, out var acol)) {


            switch (c.Op) {
                case "=":
                case "!=":
                case ">":
                case "<":
                case ">=":
                case "<=":
                    switch (c.TopLevel) {
                        case "artifact_spec_type":

                            break;
                        case "artifact_quality":
                            if (!IsFloat(c.Val)) {
                                return ("", []);
                            }
                            break;
                        default:
                            if (!IsInt(c.Val)) {
                                return ("", []);
                            }
                            break;
                    }
                    return ($"{acol} {c.Op} ?", [c.Val]);
            }
            return ("", []);
        }

        return ("", []);
    }

    public static string GroupByColumn(string groupBy) => groupBy switch {
        "ship_type" => "m.ship",
        "duration_type" => "m.duration_type",
        "level" => "m.level",
        "mission_type" => "m.mission_type",
        "mission_target" => "m.target",
        "artifact_name" => "d.artifact_id",
        "rarity" => "d.rarity",
        "tier" => "d.level",
        "spec_type" => "d.spec_type",
        _ => "",
    };

    public static string TimeBucketFormat(string timeBucket, string customUnit) {
        return timeBucket switch {
            "day" => "%Y-%m-%d",
            "week" => "%Y-%W",
            "month" => "%Y-%m",
            "year" => "%Y",
            "custom" => customUnit switch {
                "week" => "%Y-%W",
                "month" => "%Y-%m",
                _ => "%Y-%m-%d",
            },
            _ => "%Y-%m",
        };
    }

    public static (string cond, object? modifier) CustomWindowCondition(int n, string unit) {
        if (n <= 0) {
            return ("", null);
        }
        string modifier;
        switch (unit) {
            case "day":
                modifier = $"-{n} days";
                break;
            case "week":
                modifier = $"-{n * 7} days";
                break;
            case "month":
                modifier = $"-{n} months";
                break;
            default:
                return ("", null);
        }
        return ("m.start_timestamp >= strftime('%s', 'now', ?)", modifier);
    }

    internal struct QueryBuilderSpec {
        public string Indent;
        public string SelectCols;
        public bool ArtifactSrc;
        public string Where;
        public List<string>? ExtraWhere;
        public string GroupBy;
        public string OrderBy;

        public readonly string Build() {
            var in_ = Indent;

            var from = "mission m";
            if (ArtifactSrc) {
                from = "artifact_drops d\n" + in_ + "JOIN mission m ON d.mission_id = m.mission_id AND d.player_id = m.player_id";
            }

            var where = Where;
            if (ArtifactSrc) {
                where += " AND d.drop_index >= 0";
            }
            if (ExtraWhere != null) {
                foreach (var c in ExtraWhere) {
                    where += " AND " + c;
                }
            }

            return "\n" + in_ + "SELECT " + SelectCols +
                "\n" + in_ + "FROM " + from +
                "\n" + in_ + "WHERE " + where +
                "\n" + in_ + "GROUP BY " + GroupBy +
                "\n" + in_ + "ORDER BY " + OrderBy;
        }
    }


    private const string WeightedCapWeightSelect = "SUM(CASE WHEN m.nominal_capacity > 0 AND m.capacity > 0\n" +
        "                        THEN CAST(m.nominal_capacity AS REAL) / CAST(m.capacity AS REAL)\n" +
        "                        ELSE 1.0 END) AS cap_weight";

    public static (string query, List<object?> args) BuildAggregateQuery(
        ReportDefinition def, string baseWhere, IReadOnlyList<object?> baseArgs) {
        var args = new List<object?>(baseArgs);
        var groupCol = GroupByColumn(def.GroupBy);
        var query = new QueryBuilderSpec {
            Indent = "            ",
            SelectCols = $"CAST({groupCol} AS TEXT), COUNT(*) AS count",
            ArtifactSrc = def.Subject == "artifacts",
            Where = baseWhere,
            GroupBy = groupCol,
            OrderBy = "count DESC",
        }.Build();
        return (query, args);
    }

    public static (string query, List<object?> args) BuildTimeSeriesQuery(
        ReportDefinition def, string baseWhere, IReadOnlyList<object?> baseArgs) {
        var args = new List<object?>(baseArgs);
        var format = TimeBucketFormat(def.TimeBucket, def.CustomBucketUnit);
        var bucketExpr = "strftime('" + format + "', datetime(m.start_timestamp, 'unixepoch'))";

        List<string>? extraWhere = null;
        if (def.TimeBucket == "custom") {
            var (cond, modifier) = CustomWindowCondition(def.CustomBucketN, def.CustomBucketUnit);
            if (cond != "") {
                extraWhere = [cond];
                args.Add(modifier);
            }
        }

        var query = new QueryBuilderSpec {
            Indent = "            ",
            SelectCols = $"{bucketExpr} AS bucket, COUNT(*) AS count",
            ArtifactSrc = def.Subject == "artifacts",
            Where = baseWhere,
            ExtraWhere = extraWhere,
            GroupBy = "bucket",
            OrderBy = "bucket ASC",
        }.Build();
        return (query, args);
    }

    public static (string query, List<object?> args) BuildPivotQuery(
        ReportDefinition def, string baseWhere, IReadOnlyList<object?> args) {
        if (def.TimeBucket != "" && def.Mode == "time_series") {
            throw new InvalidOperationException(
                "time-series pivot is not supported; clear the time bucket or remove the secondary group-by");
        }
        var col1 = GroupByColumn(def.GroupBy);
        var col2 = GroupByColumn(def.SecondaryGroupBy);
        if (col1 == "") {
            throw new InvalidOperationException($"unsupported group-by dimension \"{def.GroupBy}\"");
        }
        if (col2 == "") {
            throw new InvalidOperationException($"unsupported secondary group-by dimension \"{def.SecondaryGroupBy}\"");
        }
        var outArgs = new List<object?>(args);
        var needsArtifactJoin = col1.StartsWith("d.", StringComparison.Ordinal) || col2.StartsWith("d.", StringComparison.Ordinal);
        var query = new QueryBuilderSpec {
            Indent = "            ",
            SelectCols = $"CAST({col1} AS TEXT), CAST({col2} AS TEXT), COUNT(*) AS count",
            ArtifactSrc = needsArtifactJoin,
            Where = baseWhere,
            GroupBy = $"{col1}, {col2}",
            OrderBy = $"{col1}, {col2}",
        }.Build();
        return (query, outArgs);
    }

    public static (string query, List<object?> args) BuildTimePivotQuery(
        ReportDefinition def, string baseWhere, IReadOnlyList<object?> args) {
        var col2 = GroupByColumn(def.SecondaryGroupBy);
        if (col2 == "") {
            throw new InvalidOperationException($"unsupported secondary group-by dimension \"{def.SecondaryGroupBy}\"");
        }

        var format = TimeBucketFormat(def.TimeBucket, def.CustomBucketUnit);
        var bucketExpr = "strftime('" + format + "', datetime(m.start_timestamp, 'unixepoch'))";
        var needsArtifactJoin = col2.StartsWith("d.", StringComparison.Ordinal) || baseWhere.Contains("d.", StringComparison.Ordinal);

        var outArgs = new List<object?>(args);

        List<string>? extraWhere = null;
        if (def.TimeBucket == "custom") {
            var (cond, modifier) = CustomWindowCondition(def.CustomBucketN, def.CustomBucketUnit);
            if (cond != "") {
                extraWhere = [cond];
                outArgs.Add(modifier);
            }
        }

        var query = new QueryBuilderSpec {
            Indent = "            ",
            SelectCols = $"{bucketExpr} AS bucket, CAST({col2} AS TEXT) AS grp, COUNT(*) AS count",
            ArtifactSrc = needsArtifactJoin,
            Where = baseWhere,
            ExtraWhere = extraWhere,
            GroupBy = "bucket, grp",
            OrderBy = "bucket ASC, grp ASC",
        }.Build();
        return (query, outArgs);
    }

    public static (string clause, List<object?> args) FamilyWeightClause(IReadOnlyList<int> ids) {
        if (ids.Count == 0) {
            return ("", []);
        }
        var placeholders = new string[ids.Count];
        var args = new List<object?>(ids.Count);
        for (var i = 0; i < ids.Count; i++) {
            placeholders[i] = "?";
            args.Add(ids[i]);
        }
        return ("d.artifact_id IN (" + string.Join(", ", placeholders) + ")", args);
    }

    public static (string query, List<object?> args) BuildWeightedAggregateQuery(
        ReportDefinition def, string baseWhere, IReadOnlyList<object?> baseArgs,
        string fwClause, IReadOnlyList<object?> fwArgs) {
        var args = new List<object?>(baseArgs);
        args.AddRange(fwArgs);

        var groupCol = GroupByColumn(def.GroupBy);
        var where = baseWhere + " AND " + fwClause;

        var query = new QueryBuilderSpec {
            Indent = "        ",
            SelectCols = $"CAST({groupCol} AS TEXT), CAST(d.artifact_id AS INTEGER), CAST(d.level AS INTEGER),\n               {WeightedCapWeightSelect}",
            ArtifactSrc = true,
            Where = where,
            GroupBy = $"{groupCol}, d.artifact_id, d.level",
            OrderBy = "cap_weight DESC",
        }.Build();

        return (query, args);
    }

    public static (string query, List<object?> args) BuildWeightedPivotQuery(
        ReportDefinition def, string baseWhere, IReadOnlyList<object?> baseArgs,
        string fwClause, IReadOnlyList<object?> fwArgs) {
        var col1 = GroupByColumn(def.GroupBy);
        var col2 = GroupByColumn(def.SecondaryGroupBy);
        if (col1 == "") {
            throw new InvalidOperationException($"unsupported group-by dimension \"{def.GroupBy}\"");
        }
        if (col2 == "") {
            throw new InvalidOperationException($"unsupported secondary group-by dimension \"{def.SecondaryGroupBy}\"");
        }

        var args = new List<object?>(baseArgs);
        args.AddRange(fwArgs);

        var where = baseWhere + " AND " + fwClause;
        var query = new QueryBuilderSpec {
            Indent = "        ",
            SelectCols = $"CAST({col1} AS TEXT), CAST({col2} AS TEXT), CAST(d.artifact_id AS INTEGER), CAST(d.level AS INTEGER),\n               {WeightedCapWeightSelect}",
            ArtifactSrc = true,
            Where = where,
            GroupBy = $"{col1}, {col2}, d.artifact_id, d.level",
            OrderBy = $"{col1}, {col2}",
        }.Build();

        return (query, args);
    }

    public static (string query, List<object?> args) BuildWeightedTimeSeriesQuery(
        ReportDefinition def, string baseWhere, IReadOnlyList<object?> baseArgs,
        string fwClause, IReadOnlyList<object?> fwArgs) {
        var args = new List<object?>(baseArgs);
        args.AddRange(fwArgs);

        var format = TimeBucketFormat(def.TimeBucket, def.CustomBucketUnit);
        var bucketExpr = "strftime('" + format + "', datetime(m.start_timestamp, 'unixepoch'))";
        var where = baseWhere + " AND " + fwClause;

        List<string>? extraWhere = null;
        if (def.TimeBucket == "custom") {
            var (cond, modifier) = CustomWindowCondition(def.CustomBucketN, def.CustomBucketUnit);
            if (cond != "") {
                extraWhere = [cond];
                args.Add(modifier);
            }
        }

        var query = new QueryBuilderSpec {
            Indent = "        ",
            SelectCols = $"{bucketExpr} AS bucket, CAST(d.artifact_id AS INTEGER), CAST(d.level AS INTEGER),\n               {WeightedCapWeightSelect}",
            ArtifactSrc = true,
            Where = where,
            ExtraWhere = extraWhere,
            GroupBy = "bucket, d.artifact_id, d.level",
            OrderBy = "bucket ASC",
        }.Build();

        return (query, args);
    }

    public static (string query, List<object?> args) BuildWeightedTimePivotQuery(
        ReportDefinition def, string baseWhere, IReadOnlyList<object?> baseArgs,
        string fwClause, IReadOnlyList<object?> fwArgs) {
        var col2 = GroupByColumn(def.SecondaryGroupBy);
        if (col2 == "") {
            throw new InvalidOperationException($"unsupported secondary group-by dimension \"{def.SecondaryGroupBy}\"");
        }

        var args = new List<object?>(baseArgs);
        args.AddRange(fwArgs);

        var format = TimeBucketFormat(def.TimeBucket, def.CustomBucketUnit);
        var bucketExpr = "strftime('" + format + "', datetime(m.start_timestamp, 'unixepoch'))";
        var where = baseWhere + " AND " + fwClause;

        List<string>? extraWhere = null;
        if (def.TimeBucket == "custom") {
            var (cond, modifier) = CustomWindowCondition(def.CustomBucketN, def.CustomBucketUnit);
            if (cond != "") {
                extraWhere = [cond];
                args.Add(modifier);
            }
        }

        var query = new QueryBuilderSpec {
            Indent = "        ",
            SelectCols = $"{bucketExpr} AS bucket, CAST({col2} AS TEXT) AS grp, CAST(d.artifact_id AS INTEGER), CAST(d.level AS INTEGER),\n               {WeightedCapWeightSelect}",
            ArtifactSrc = true,
            Where = where,
            ExtraWhere = extraWhere,
            GroupBy = "bucket, grp, d.artifact_id, d.level",
            OrderBy = "bucket ASC, grp ASC",
        }.Build();

        return (query, args);
    }
}
