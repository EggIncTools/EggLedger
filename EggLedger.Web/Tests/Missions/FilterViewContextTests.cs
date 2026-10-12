using EggIdentity.UI;
using EggLedger.Web.Missions;
using EggLedger.Web.Missions.Model;
using EggLedger.Web.State;

namespace EggLedger.Web.Tests.Missions;

public sealed class FilterViewContextTests {
    private static readonly FilterFieldCtx Fields = new();

    private static FilterSet And(params (string Field, string Op, string Val)[] conditions) =>
        new([.. conditions.Select(c => new FilterCondition(c.Field, c.Op, c.Val))], []);

    [Fact]
    public void Resolve_ShipsAndDrops_ReturnTheSameDraft() {
        var state = new FilterState();

        var fromShips = state.Resolve(new ShipsFilterContext(Fields), null);
        var fromDrops = state.Resolve(new DropsFilterContext(Fields), null);

        Assert.Same(fromShips, fromDrops);
    }

    [Fact]
    public void Resolve_SharedContext_KeepsDraftRows_AndIgnoresInitialConditions() {
        var state = new FilterState();
        var ships = new ShipsFilterContext(Fields);

        var draft = state.Resolve(ships, null);
        draft.Rows[0].Head = new FilterCondition("ship", "=", "3");

        var again = state.Resolve(ships, And(("level", "=", "9")));

        Assert.Same(draft, again);
        Assert.Equal("ship", again.Rows[0].Head.Field);
        Assert.Equal("3", again.Rows[0].Head.Value);
    }

    [Fact]
    public void Resolve_ReportsContext_HydratesPerReport_AndLeavesSharedDraftAlone() {
        var state = new FilterState();
        var reports = new ReportsFilterContext(Fields);
        var shared = state.Resolve(new ShipsFilterContext(Fields), null);
        shared.Rows[0].Head = new FilterCondition("ship", "", "");

        var first = state.Resolve(reports, And(("level", "=", "9")));
        var second = state.Resolve(reports, And(("target", "=", "4")));

        Assert.NotSame(first, second);
        Assert.NotSame(shared, first);
        Assert.Equal("level", first.Rows[0].Head.Field);
        Assert.Equal("target", second.Rows[0].Head.Field);
        Assert.Equal("ship", shared.Rows[0].Head.Field);
    }

    [Fact]
    public void Resolve_AlwaysLeavesATrailingBlankRow() {
        var state = new FilterState();

        var shared = state.Resolve(new ShipsFilterContext(Fields), null);
        var perReport = state.Resolve(new ReportsFilterContext(Fields), And(("ship", "=", "3")));

        Assert.Equal("", shared.Rows[^1].Head.Field);
        Assert.Equal(2, perReport.Rows.Count);
        Assert.Equal("", perReport.Rows[^1].Head.Field);
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

        var shipsDraft = state.Resolve(ships, null);
        shipsDraft.Hydrate(And(("ship", "=", "3")));

        var dropsDraft = state.Resolve(drops, null);
        Assert.Equal("ship", dropsDraft.Rows[0].Head.Field);

        dropsDraft.Rows[0].Head = dropsDraft.Rows[0].Head with { Value = "5" };

        var backToShips = state.Resolve(ships, null);
        Assert.Same(shipsDraft, backToShips);
        Assert.Equal("5", backToShips.Rows[0].Head.Value);
    }

    [Fact]
    public void Complete_KeepsOrSiblingsWithTheirRow() {
        var draft = new FilterDraft();
        draft.Hydrate(new FilterSet(
            [new FilterCondition("ship", "=", "3"), new FilterCondition("level", "=", "5")],
            [[], [new FilterCondition("level", "=", "8")]]));

        var set = draft.Complete(LedgerFilterFields.ReportFilterFields);

        Assert.Equal(2, set.And.Count);
        Assert.Empty(set.Or[0]);
        Assert.Equal("8", set.Or[1][0].Value);
    }

    [Fact]
    public void Complete_SkipsIncompleteRows() {
        var draft = new FilterDraft();
        draft.Hydrate(And(("ship", "=", "3"), ("level", "=", ""), ("", "", "")));

        var set = draft.Complete(LedgerFilterFields.ReportFilterFields);

        Assert.Single(set.And);
        Assert.Equal("ship", set.And[0].Field);
    }

    [Fact]
    public void Hash_SeparatesOrSiblingFromASecondAndRow() {
        var withSibling = new FilterSet([new FilterCondition("ship", "=", "3")], [[new FilterCondition("level", "=", "8")]]);
        var twoRows = And(("ship", "=", "3"), ("level", "=", "8"));

        Assert.NotEqual(FilterCodec.Hash("UTC", withSibling), FilterCodec.Hash("UTC", twoRows));
    }

    [Fact]
    public void Hash_DiffersByScope() {
        var set = And(("ship", "=", "3"));
        Assert.NotEqual(FilterCodec.Hash("UTC", set), FilterCodec.Hash("America/New_York", set));
    }
}
