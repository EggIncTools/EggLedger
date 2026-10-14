using System.Globalization;
using EggIdentity.Filters;
using EggIdentity.Reports;

namespace EggLedger.Domain.Reports;

public sealed class LedgerReportCost(TimeProvider? time = null) : IReportCostEstimator {
    public ReportCost Estimate(ReportQuery query) {
        var clock = time ?? TimeProvider.System;
        var isTimeSeries = query.GroupBy.Any(g => g.Bucket is not null);
        var hasSecondaryGroup = query.GroupBy.Count >= 2;

        if (hasSecondaryGroup && isTimeSeries) {
            var eitherIsArtifact = IsEitherArtifact(query);
            if (eitherIsArtifact) return new ReportCost(HasDateFilter(query.Filters) ? ReportCostLevel.Medium : ReportCostLevel.High);
            if (!HasDateFilter(query.Filters)) return new ReportCost(ReportCostLevel.High);
            return new ReportCost(DateFilterWindowDays(query.Filters, clock) > 90 ? ReportCostLevel.High : ReportCostLevel.Medium);
        }

        if (hasSecondaryGroup) {
            var eitherIsArtifact = IsEitherArtifact(query);
            if (eitherIsArtifact) return new ReportCost(HasDateFilter(query.Filters) ? ReportCostLevel.Medium : ReportCostLevel.High);
            return new ReportCost(ReportCostLevel.Low);
        }

        if (isTimeSeries) {
            if (TryWithinDays(query.Filters, out var withinDays)) {
                return new ReportCost(withinDays > 90 ? ReportCostLevel.High : ReportCostLevel.Medium);
            }
            if (!HasDateFilter(query.Filters)) return new ReportCost(ReportCostLevel.High);
            var d = DateFilterWindowDays(query.Filters, clock);
            return new ReportCost(d > 90 ? ReportCostLevel.High : ReportCostLevel.Medium);
        }

        return new ReportCost(HasArtifactScopeFilter(query.Filters) ? ReportCostLevel.Medium : ReportCostLevel.Low);
    }

    private static bool IsEitherArtifact(ReportQuery query) =>
        IsArtifactDimension(query.GroupBy[0].Dimension) || IsArtifactDimension(query.GroupBy[1].Dimension);

    private static bool TryWithinDays(FilterSet filters, out int days) {
        days = 0;
        foreach (var c in filters.And) {
            if (c.Field != LedgerReportKeys.Launched) continue;
            if (FilterOps.Canonical(c.Op) != FilterOps.Within) continue;
            if (TryParseWithinValue(c.Value, out days)) return true;
        }
        return false;
    }

    private static bool TryParseWithinValue(string value, out int days) {
        days = 0;
        if (string.IsNullOrEmpty(value)) return false;
        var unit = value[^1];
        if (!int.TryParse(value[..^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) return false;
        days = unit switch {
            'd' => n,
            'w' => n * 7,
            'm' => n * 30,
            'y' => n * 365,
            _ => -1,
        };
        return days >= 0;
    }

    private static bool IsDateField(FilterCondition c) =>
        c.Field == LedgerReportKeys.Launched || c.Field == LedgerReportKeys.Returned;

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
            var daysSince = (int)((now - t).TotalHours / 24);
            if (daysSince < minDays) minDays = daysSince;
        }
        return minDays;
    }

    private static bool HasArtifactScopeFilter(FilterSet f) =>
        LedgerFilters.Conditions(f).Any(c => LedgerFilters.IsArtifactField(c.Field));

    private static bool IsArtifactDimension(string dimension) => dimension switch {
        LedgerReportKeys.ArtifactName or LedgerReportKeys.Rarity or LedgerReportKeys.Tier or LedgerReportKeys.SpecType => true,
        _ => false,
    };
}
