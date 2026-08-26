using EggLedger.Web.Missions;
using EggLedger.Web.Missions.Model;
using EggLedger.Web.State;

namespace EggLedger.Web.Tests.Missions;

public sealed class FilterViewContextTests {
    private static readonly FilterFieldCtx Fields = new();

    private static List<FilterCondition> And(params (string Field, string Op, string Val)[] conditions) =>
        [.. conditions.Select(c => new FilterCondition(c.Field, c.Op, c.Val))];

    [Fact]
    public void Resolve_ShipsAndDrops_ReturnTheSameDraft() {
        var state = new FilterState();

        var fromShips = state.Resolve(new ShipsFilterContext(Fields), null, null);
        var fromDrops = state.Resolve(new DropsFilterContext(Fields), null, null);

        Assert.Same(fromShips, fromDrops);
    }

    [Fact]
    public void Resolve_SharedContext_KeepsDraftRows_AndIgnoresInitialConditions() {
        var state = new FilterState();
        var ships = new ShipsFilterContext(Fields);

        var draft = state.Resolve(ships, null, null);
        draft.Rows[0].Cond.TopLevel = "ship";
        draft.Rows[0].Cond.Op = "eq";
        draft.Rows[0].Cond.Val = "3";

        var again = state.Resolve(ships, And(("level", "eq", "9")), null);

        Assert.Same(draft, again);
        Assert.Equal("ship", again.Rows[0].Cond.TopLevel);
        Assert.Equal("3", again.Rows[0].Cond.Val);
    }

    [Fact]
    public void Resolve_ReportsContext_HydratesPerReport_AndLeavesSharedDraftAlone() {
        var state = new FilterState();
        var reports = new ReportsFilterContext(Fields);
        var shared = state.Resolve(new ShipsFilterContext(Fields), null, null);
        shared.Rows[0].Cond.TopLevel = "ship";

        var first = state.Resolve(reports, And(("level", "eq", "9")), null);
        var second = state.Resolve(reports, And(("target", "eq", "4")), null);

        Assert.NotSame(first, second);
        Assert.NotSame(shared, first);
        Assert.Equal("level", first.Rows[0].Cond.TopLevel);
        Assert.Equal("target", second.Rows[0].Cond.TopLevel);
        Assert.Equal("ship", shared.Rows[0].Cond.TopLevel);
        Assert.Single(shared.Rows);
    }

    [Fact]
    public void Resolve_AlwaysLeavesATrailingBlankRow() {
        var state = new FilterState();

        var shared = state.Resolve(new ShipsFilterContext(Fields), null, null);
        var perReport = state.Resolve(new ReportsFilterContext(Fields), And(("ship", "eq", "3")), null);

        Assert.Equal("", shared.Rows[^1].Cond.TopLevel);
        Assert.Equal(2, perReport.Rows.Count);
        Assert.Equal("", perReport.Rows[^1].Cond.TopLevel);
    }

    [Fact]
    public void BindsLike_IsFalse_WhenArtifactFieldsDiffer() {
        var ships = new ShipsFilterContext(Fields);

        Assert.True(ships.BindsLike(new DropsFilterContext(Fields)));
        Assert.False(ships.BindsLike(new ReportsFilterContext(Fields)));
    }

    [Fact]
    public void BindsLike_IsFalse_WhenFieldContextIsADifferentInstance() {
        var ships = new ShipsFilterContext(Fields);

        Assert.False(ships.BindsLike(new DropsFilterContext(new FilterFieldCtx())));
    }

    [Fact]
    public void Draft_SurvivesViewSwitch() {
        var state = new FilterState();
        var ships = new ShipsFilterContext(Fields);
        var drops = new DropsFilterContext(Fields);

        var shipsDraft = state.Resolve(ships, null, null);
        shipsDraft.Hydrate(And(("ship", "eq", "3")), null);

        var dropsDraft = state.Resolve(drops, null, null);
        Assert.Equal("ship", dropsDraft.Rows[0].Cond.TopLevel);

        dropsDraft.Rows[0].Cond.Val = "5";

        var backToShips = state.Resolve(ships, null, null);
        Assert.Same(shipsDraft, backToShips);
        Assert.Equal("5", backToShips.Rows[0].Cond.Val);
    }

    [Fact]
    public void Hydrate_RestoresOrSiblings() {
        var draft = new FilterDraft();
        draft.Hydrate(
            And(("ship", "eq", "3")),
            [And(("ship", "eq", "4"))]);

        Assert.Single(draft.Rows);
        Assert.Single(draft.Rows[0].Or);
        Assert.Equal("4", draft.Rows[0].Or[0].Val);
    }

    [Fact]
    public void CompleteConditions_SkipsIncompleteRows() {
        var draft = new FilterDraft();
        draft.Hydrate(
            And(("ship", "eq", "3"), ("level", "eq", ""), ("", "", "")),
            null);

        var (and, or) = draft.CompleteConditions();

        Assert.Single(and);
        Assert.Equal("ship", and[0].TopLevel);
        Assert.Single(or);
        Assert.Null(or[0]);
    }

    [Fact]
    public void DraftHash_IsStableAcrossEquivalentDrafts() {
        var first = new FilterDraft();
        first.Hydrate(
            And(("ship", "eq", "3"), ("level", "gte", "5")),
            [null, And(("level", "eq", "8"))]);

        var second = new FilterDraft();
        second.Hydrate(
            And(("ship", "eq", "3"), ("level", "gte", "5")),
            [null, And(("level", "eq", "8"))]);
        second.EnsureTrailingRow();

        Assert.Equal(FilterMatching.Hash("UTC", first), FilterMatching.Hash("UTC", second));
    }

    [Fact]
    public void DraftHash_DiffersForDifferentConditions() {
        var baseline = new FilterDraft();
        baseline.Hydrate(And(("ship", "eq", "3")), null);

        var otherValue = new FilterDraft();
        otherValue.Hydrate(And(("ship", "eq", "4")), null);

        var otherOp = new FilterDraft();
        otherOp.Hydrate(And(("ship", "gte", "3")), null);

        var otherField = new FilterDraft();
        otherField.Hydrate(And(("level", "eq", "3")), null);

        var hash = FilterMatching.Hash("UTC", baseline);
        Assert.NotEqual(hash, FilterMatching.Hash("UTC", otherValue));
        Assert.NotEqual(hash, FilterMatching.Hash("UTC", otherOp));
        Assert.NotEqual(hash, FilterMatching.Hash("UTC", otherField));
    }

    [Fact]
    public void DraftHash_SeparatesOrSiblingFromASecondAndRow() {
        var withSibling = new FilterDraft();
        withSibling.Hydrate(
            And(("ship", "eq", "3")),
            [And(("level", "eq", "8"))]);

        var twoRows = new FilterDraft();
        twoRows.Hydrate(And(("ship", "eq", "3"), ("level", "eq", "8")), null);

        Assert.NotEqual(FilterMatching.Hash("UTC", withSibling), FilterMatching.Hash("UTC", twoRows));
    }

    [Fact]
    public void DraftHash_DiffersByScope() {
        var draft = new FilterDraft();
        draft.Hydrate(And(("ship", "eq", "3")), null);

        Assert.NotEqual(FilterMatching.Hash("UTC", draft), FilterMatching.Hash("America/New_York", draft));
    }
}
