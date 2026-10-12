using EggIdentity.UI;
using EggLedger.Web.Missions.Model;

namespace EggLedger.Web.State;

public sealed class FilterState {
    private readonly Dictionary<string, FilterDraft> _drafts = [with(StringComparer.Ordinal)];

    public FilterDraft Resolve(FilterViewContext context, FilterSet? initial) {
        FilterDraft draft;
        if (context.UsesSharedDraft) {
            draft = GetDraft(context.DraftKey);
        } else {
            draft = new FilterDraft();
            draft.Hydrate(initial ?? FilterSet.Empty);
        }

        draft.EnsureTrailingRow();
        return draft;
    }

    public FilterDraft GetDraft(string key) {
        if (_drafts.TryGetValue(key, out var existing)) return existing;

        var draft = new FilterDraft();
        _drafts[key] = draft;
        return draft;
    }
}
