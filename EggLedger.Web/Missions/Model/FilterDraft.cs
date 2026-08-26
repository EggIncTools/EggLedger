using WebCondition = EggLedger.Web.Missions.FilterCondition;

namespace EggLedger.Web.Missions.Model;

public sealed class FilterDraftRow {
    public WebCondition Cond { get; } = new();

    public List<WebCondition> Or { get; } = [];
}

public sealed class FilterDraft {
    public List<FilterDraftRow> Rows { get; } = [];

    public static bool IsComplete(WebCondition c) {
        return FilterFields.GetReportField(c.TopLevel) is not null
               && !string.IsNullOrEmpty(c.Op)
               && c.Val.Length > 0;
    }

    public (List<WebCondition> And, List<IReadOnlyList<WebCondition>?> Or) CompleteConditions() {
        var and = new List<WebCondition>();
        var or = new List<IReadOnlyList<WebCondition>?>();

        foreach (var row in Rows) {
            if (!IsComplete(row.Cond)) {
                continue;
            }

            and.Add(CloneOf(row.Cond));
            var siblings = new List<WebCondition>();
            foreach (var sibling in row.Or) {
                if (IsComplete(sibling)) {
                    siblings.Add(CloneOf(sibling));
                }
            }

            or.Add(siblings.Count > 0 ? siblings : null);
        }

        return (and, or);
    }

    public void Hydrate(
        IReadOnlyList<WebCondition>? and,
        IReadOnlyList<IReadOnlyList<WebCondition>?>? or) {
        Rows.Clear();
        if (and is null) {
            return;
        }

        for (var i = 0; i < and.Count; i++) {
            var row = new FilterDraftRow();
            Copy(and[i], row.Cond);
            var siblings = or is not null && i < or.Count ? or[i] : null;
            if (siblings is not null) {
                foreach (var sibling in siblings) {
                    var copy = new WebCondition();
                    Copy(sibling, copy);
                    row.Or.Add(copy);
                }
            }

            Rows.Add(row);
        }
    }

    public void EnsureTrailingRow() {
        if (Rows.Count == 0 || !string.IsNullOrEmpty(Rows[^1].Cond.TopLevel)) {
            Rows.Add(new FilterDraftRow());
        }
    }

    public void RemoveRow(int index) {
        Rows.RemoveAt(index);
        if (Rows.Count == 0) {
            Rows.Add(new FilterDraftRow());
        }
    }

    private static WebCondition CloneOf(WebCondition c) {
        return new WebCondition(c.TopLevel, c.Op, c.Val);
    }

    private static void Copy(WebCondition from, WebCondition to) {
        to.TopLevel = from.TopLevel;
        to.Op = from.Op;
        to.Val = from.Val;
    }
}
