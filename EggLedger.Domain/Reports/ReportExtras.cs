using EggIdentity.Filters;
using EggIdentity.Reports;

namespace EggLedger.Domain.Reports;

public static class ReportExtras {
    public static string Family(ReportDefinition def) {
        ArgumentNullException.ThrowIfNull(def);
        return def.Query.Filters.And.FirstOrDefault(c => c.Field == LedgerReportKeys.Family)?.Value ?? "";
    }

    public static ReportDefinition WithFamily(ReportDefinition def, string family) {
        ArgumentNullException.ThrowIfNull(def);
        ArgumentNullException.ThrowIfNull(family);
        var query = def.Query;
        List<FilterCondition> and = [.. query.Filters.And.Where(c => c.Field != LedgerReportKeys.Family)];
        List<MeasureRequest> measures = [.. query.Measures.Where(m => m.Key != LedgerReportKeys.CraftingWeight)];
        if (family.Length > 0) {
            and.Add(new FilterCondition(LedgerReportKeys.Family, FilterOps.Eq, family));
            measures.Insert(0, new MeasureRequest(LedgerReportKeys.CraftingWeight, Aggregation.Sum));
        }
        return def with { Query = query with { Filters = query.Filters with { And = and }, Measures = measures } };
    }
}
