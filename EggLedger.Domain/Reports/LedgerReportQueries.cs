using EggIdentity.Reports;

namespace EggLedger.Domain.Reports;

public static class LedgerReportQueries {
    public static ReportQuery Prepare(ReportQuery query) {
        ArgumentNullException.ThrowIfNull(query);
        return query with { Normalize = PerMission(query.Normalize, query.Source, query.GroupBy) };
    }

    public static Normalize PerMission(Normalize normalize, string source, IReadOnlyList<GroupKey> groupBy) {
        ArgumentNullException.ThrowIfNull(normalize);
        ArgumentNullException.ThrowIfNull(groupBy);
        if (normalize.Kind != NormalizeKind.By || normalize.Source is not null) return normalize;
        if (normalize.Measure is not (LedgerReportKeys.Airtime or LedgerReportKeys.Launches)) return normalize;
        if (source == LedgerReportKeys.Missions) return normalize;
        if (!groupBy.All(g => LedgerReportSources.Missions.Dimension(g.Dimension) is not null)) return normalize;
        return Normalize.By(normalize.Measure, LedgerReportKeys.Missions);
    }

    public static string? UnitFor(Normalize normalize) {
        ArgumentNullException.ThrowIfNull(normalize);
        return normalize.Kind switch {
            NormalizeKind.Row or NormalizeKind.Column or NormalizeKind.Global => "%",
            NormalizeKind.By when normalize.Measure == LedgerReportKeys.Launches => "per launch",
            NormalizeKind.By when normalize.Measure == LedgerReportKeys.Airtime => "per flight hour",
            _ => null,
        };
    }
}
