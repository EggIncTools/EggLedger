using System.Globalization;
using EggIdentity.Filters;

namespace EggLedger.Domain.Reports;

public static class Weight {
    public static string ClassifyWeight(ReportDefinition def, TimeProvider? time = null) {
        var clock = time ?? TimeProvider.System;

        if (def.SecondaryGroupBy != "" && def.Mode == "time_series") {
            var eitherIsArtifact = IsArtifactDimension(def.GroupBy) || IsArtifactDimension(def.SecondaryGroupBy);
            if (eitherIsArtifact) return HasDateFilter(def.Filters) ? "MEDIUM" : "HEAVY";
            if (!HasDateFilter(def.Filters)) return "HEAVY";
            return DateFilterWindowDays(def.Filters, clock) > 90 ? "HEAVY" : "MEDIUM";
        }

        if (def.SecondaryGroupBy != "") {
            var eitherIsArtifact = IsArtifactDimension(def.GroupBy) || IsArtifactDimension(def.SecondaryGroupBy);
            if (eitherIsArtifact) return HasDateFilter(def.Filters) ? "MEDIUM" : "HEAVY";
            return "LOW";
        }

        if (def.Mode == "time_series") {
            if (def.TimeBucket == "custom") {
                var days = CustomBucketDays(def.CustomBucketN, def.CustomBucketUnit);
                return days > 90 ? "HEAVY" : "MEDIUM";
            }
            if (!HasDateFilter(def.Filters)) return "HEAVY";
            var d = DateFilterWindowDays(def.Filters, clock);
            return d > 90 ? "HEAVY" : "MEDIUM";
        }

        return def.Mode == "aggregate" && HasArtifactScopeFilter(def.Filters) ? "MEDIUM" : "LOW";
    }

    private static int CustomBucketDays(int n, string unit) => unit switch {
        "day" => n,
        "week" => n * 7,
        "month" => n * 30,
        _ => n,
    };

    private static bool IsDateField(FilterCondition c) => c.Field is "launchDT" or "returnDT";

    private static bool HasDateFilter(FilterSet f) => LedgerFilters.Conditions(f).Any(IsDateField);

    private static int DateFilterWindowDays(FilterSet f, TimeProvider clock) {
        var minDays = 9999;
        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var c in LedgerFilters.Conditions(f)) {
            if (!IsDateField(c)) continue;
            if (FilterOps.Canonical(c.Op) is not FilterOps.Gt and not FilterOps.Gte) {
                continue;
            }
            if (!DateTime.TryParseExact(c.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var t)) {
                continue;
            }
            var days = (int)((now - t).TotalHours / 24);
            if (days < minDays) minDays = days;
        }
        return minDays;
    }

    private static bool HasArtifactScopeFilter(FilterSet f) =>
        LedgerFilters.Conditions(f).Any(c => c.Field.StartsWith("artifact_", StringComparison.Ordinal));

    private static bool IsArtifactDimension(string groupBy) => groupBy switch {
        "artifact_name" or "rarity" or "tier" or "spec_type" => true,
        _ => false,
    };
}
