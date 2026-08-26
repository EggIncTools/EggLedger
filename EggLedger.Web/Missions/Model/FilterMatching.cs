using System.Text;
using EggLedger.Domain.MissionPacking;
using WebCondition = EggLedger.Web.Missions.FilterCondition;

namespace EggLedger.Web.Missions.Model;

public static class FilterMatching {
    public const string FailedText = "Filter failed, please try again";

    public static string Hash(string scope, FilterDraft draft) {
        var (and, or) = draft.CompleteConditions();
        return Hash(scope, and, or);
    }

    public static string Hash(
        string scope,
        IReadOnlyList<WebCondition> and,
        IReadOnlyList<IReadOnlyList<WebCondition>?> or) {
        var sb = new StringBuilder(scope);
        sb.Append('#');
        for (var i = 0; i < and.Count; i++) {
            Append(sb, and[i]);
            var siblings = i < or.Count ? or[i] : null;
            if (siblings is not null) {
                foreach (var sibling in siblings) {
                    sb.Append('|');
                    Append(sb, sibling);
                }
            }

            sb.Append(';');
        }

        return sb.ToString();
    }

    public static async Task<IReadOnlySet<string>> MatchingIdsAsync(
        MissionFilterMatcher matcher,
        IReadOnlyList<DatabaseMission> missions,
        IReadOnlyList<WebCondition> and,
        IReadOnlyList<IReadOnlyList<WebCondition>?> or) {
        var matched = new HashSet<string>(StringComparer.Ordinal);
        foreach (var mission in missions) {
            if (await matcher.MissionMatchesFilterAsync(mission, and, or).ConfigureAwait(false)) {
                matched.Add(mission.MissiondId);
            }
        }

        return matched;
    }

    private static void Append(StringBuilder sb, WebCondition c) {
        sb.Append(c.TopLevel).Append('~').Append(c.Op).Append('~').Append(c.Val);
    }
}
