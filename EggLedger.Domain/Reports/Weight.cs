using System.Globalization;

namespace EggLedger.Domain.Reports;

public static class Weight {
    public static string ClassifyWeight(ReportDefinition def) {

        if (def.SecondaryGroupBy != "" && def.Mode == "time_series") {
            var eitherIsArtifact = IsArtifactDimension(def.GroupBy) || IsArtifactDimension(def.SecondaryGroupBy);
            if (eitherIsArtifact) {
                if (HasDateFilter(def.Filters)) {
                    return "MEDIUM";
                }
                return "HEAVY";
            }
            if (!HasDateFilter(def.Filters)) {
                return "HEAVY";
            }
            if (DateFilterWindowDays(def.Filters) > 90) {
                return "HEAVY";
            }
            return "MEDIUM";
        }


        if (def.SecondaryGroupBy != "") {
            var eitherIsArtifact = IsArtifactDimension(def.GroupBy) || IsArtifactDimension(def.SecondaryGroupBy);
            if (eitherIsArtifact) {
                if (HasDateFilter(def.Filters)) {
                    return "MEDIUM";
                }
                return "HEAVY";
            }
            return "LOW";
        }


        if (def.Mode == "time_series") {
            if (def.TimeBucket == "custom") {
                var days = CustomBucketDays(def.CustomBucketN, def.CustomBucketUnit);
                if (days > 90) {
                    return "HEAVY";
                }
                return "MEDIUM";
            }
            if (!HasDateFilter(def.Filters)) {
                return "HEAVY";
            }
            var d = DateFilterWindowDays(def.Filters);
            if (d > 90) {
                return "HEAVY";
            }
            return "MEDIUM";
        }

        if (def.Mode == "aggregate" && HasArtifactScopeFilter(def.Filters)) {
            return "MEDIUM";
        }
        return "LOW";
    }

    private static int CustomBucketDays(int n, string unit) => unit switch {
        "day" => n,
        "week" => n * 7,
        "month" => n * 30,
        _ => n,
    };

    private static bool HasDateFilter(ReportFilters f) {
        foreach (var c in f.And) {
            if (c.TopLevel is "launchDT" or "returnDT") {
                return true;
            }
        }
        foreach (var group in f.Or) {
            foreach (var c in group) {
                if (c.TopLevel is "launchDT" or "returnDT") {
                    return true;
                }
            }
        }
        return false;
    }

    private static int DateFilterWindowDays(ReportFilters f) {
        var minDays = 9999;
        var all = new List<FilterCondition>(f.And);
        foreach (var group in f.Or) {
            all.AddRange(group);
        }
        var now = DateTime.UtcNow;
        foreach (var c in all) {
            if (c.TopLevel is not "launchDT" and not "returnDT") {
                continue;
            }
            if (c.Op is not ">" and not ">=") {
                continue;
            }
            if (!DateTime.TryParseExact(c.Val, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var t)) {
                continue;
            }
            var days = (int)((now - t).TotalHours / 24);
            if (days < minDays) {
                minDays = days;
            }
        }
        return minDays;
    }

    private static bool HasArtifactScopeFilter(ReportFilters f) {
        static bool IsArtifactField(string s) {
            return s.StartsWith("artifact_", StringComparison.Ordinal);
        }

        foreach (var c in f.And) {
            if (IsArtifactField(c.TopLevel)) {
                return true;
            }
        }
        foreach (var group in f.Or) {
            foreach (var c in group) {
                if (IsArtifactField(c.TopLevel)) {
                    return true;
                }
            }
        }
        return false;
    }

    private static bool IsArtifactDimension(string groupBy) => groupBy switch {
        "artifact_name" or "rarity" or "tier" or "spec_type" => true,
        _ => false,
    };
}
