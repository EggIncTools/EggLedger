using EggLedger.Domain.MissionPacking;
using EggLedger.Domain.Reports;

namespace EggLedger.Web.Missions.Model;

public static class FilterMatching {
    public const string FailedText = "Filter failed, please try again";

    public static async Task<IReadOnlySet<string>> MatchingIdsAsync(
        MissionFilterMatcher matcher,
        IReadOnlyList<DatabaseMission> missions,
        FilterSet filter) {
        var matched = new HashSet<string>(StringComparer.Ordinal);
        foreach (var mission in missions) {
            if (await matcher.MatchesAsync(mission, filter).ConfigureAwait(false)) matched.Add(mission.MissiondId);
        }

        return matched;
    }

    public static bool HasConditions(FilterSet filter) => !filter.IsEmpty;

    public static DropMatch? CountableDrop(FilterSet filter) {
        foreach (var c in filter.And) {
            if (LedgerFilters.Find(c.Field)?.Key == "drops" && FilterOps.Canonical(c.Op) == FilterOps.Contains && c.Value.Length > 0)
                return DropMatch.Decode(c.Value);
        }

        return null;
    }
}
