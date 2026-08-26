using EggLedger.Web.Missions.Model;
using WebCondition = EggLedger.Web.Missions.FilterCondition;

namespace EggLedger.Web.State;

public sealed class FilterState {
    private readonly Dictionary<string, FilterDraft> _drafts = new(StringComparer.Ordinal);

    public FilterDraft Resolve(
        FilterViewContext context,
        IReadOnlyList<WebCondition>? initialAnd,
        IReadOnlyList<IReadOnlyList<WebCondition>?>? initialOr) {
        FilterDraft draft;
        if (context.UsesSharedDraft) {
            draft = GetDraft(context.DraftKey);
        } else {
            draft = new FilterDraft();
            draft.Hydrate(initialAnd, initialOr);
        }

        draft.EnsureTrailingRow();
        return draft;
    }

    public FilterDraft GetDraft(string key) {
        if (_drafts.TryGetValue(key, out var existing)) {
            return existing;
        }

        var draft = new FilterDraft();
        _drafts[key] = draft;
        return draft;
    }
}
