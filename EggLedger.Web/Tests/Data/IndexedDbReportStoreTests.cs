using EggLedger.Web.Data;

namespace EggLedger.Web.Tests.Data;

public sealed class IndexedDbReportStoreTests {
    private static (IndexedDbReportStore Store, FakeIndexedDb Db) Make(long now = 1000) {
        var db = new FakeIndexedDb();
        return (new IndexedDbReportStore(db, () => now), db);
    }

    private static ReportRow Report(string id, string accountId, int sortOrder = 0, string filters = "") => new() {
        Id = id,
        AccountId = accountId,
        Name = "n",
        SortOrder = sortOrder,
        Filters = filters,
    };

    [Fact]
    public async Task InsertReport_RoundTripsAllFields() {
        var (store, _) = Make(now: 4242);
        var r = new ReportRow {
            Id = "r1",
            AccountId = "EI1",
            Name = "My report",
            Subject = "ARTIFACT",
            Mode = "count",
            DisplayMode = "table",
            GroupBy = "ship",
            TimeBucket = "month",
            CustomBucketN = 3,
            CustomBucketUnit = "week",
            Filters = "{\"and\":[{\"x\":1}],\"or\":[]}",
            GridX = 1,
            GridY = 2,
            GridW = 3,
            GridH = 4,
            Weight = "HIGH",
            Color = "#abcdef",
            Description = "desc",
            ChartType = "line",
            SortOrder = 7,
            ValueFilterOp = "gt",
            ValueFilterThreshold = 1.5,
            GroupId = "g1",
            NormalizeBy = "mission",
            LabelColors = "{}",
            SecondaryGroupBy = "target",
            UnfilledColor = "#000",
            FamilyWeight = "x",
            MennoEnabled = true,
            MennoCompareMode = "overlay",
            MinSampleSize = 12,
        };

        await store.InsertReportAsync(r);
        var got = await store.RetrieveReportAsync("r1");

        Assert.NotNull(got);
        Assert.Equal("r1", got!.Id);
        Assert.Equal("EI1", got.AccountId);
        Assert.Equal("My report", got.Name);
        Assert.Equal("ARTIFACT", got.Subject);
        Assert.Equal("count", got.Mode);
        Assert.Equal("table", got.DisplayMode);
        Assert.Equal("ship", got.GroupBy);
        Assert.Equal("month", got.TimeBucket);
        Assert.Equal(3, got.CustomBucketN);
        Assert.Equal("week", got.CustomBucketUnit);
        Assert.Equal("{\"and\":[{\"x\":1}],\"or\":[]}", got.Filters);
        Assert.Equal(1, got.GridX);
        Assert.Equal(2, got.GridY);
        Assert.Equal(3, got.GridW);
        Assert.Equal(4, got.GridH);
        Assert.Equal("HIGH", got.Weight);
        Assert.Equal("#abcdef", got.Color);
        Assert.Equal("desc", got.Description);
        Assert.Equal("line", got.ChartType);
        Assert.Equal(7, got.SortOrder);
        Assert.Equal("gt", got.ValueFilterOp);
        Assert.Equal(1.5, got.ValueFilterThreshold);
        Assert.Equal("g1", got.GroupId);
        Assert.Equal("mission", got.NormalizeBy);
        Assert.Equal("{}", got.LabelColors);
        Assert.Equal("target", got.SecondaryGroupBy);
        Assert.Equal("#000", got.UnfilledColor);
        Assert.Equal("x", got.FamilyWeight);
        Assert.True(got.MennoEnabled);
        Assert.Equal("overlay", got.MennoCompareMode);
        Assert.Equal(12, got.MinSampleSize);
        Assert.Equal(4242, got.CreatedAt);
        Assert.Equal(4242, got.UpdatedAt);
    }

    [Fact]
    public async Task InsertReport_EmptyFilters_GetsDefault() {
        var (store, _) = Make();
        await store.InsertReportAsync(Report("r1", "EI1", filters: "   "));
        var got = await store.RetrieveReportAsync("r1");
        Assert.Equal("{\"and\":[],\"or\":[]}", got!.Filters);
    }

    [Fact]
    public async Task InsertReport_ValidFilters_Compacted() {
        var (store, _) = Make();
        await store.InsertReportAsync(Report("r1", "EI1", filters: "{ \"and\" : [ ] , \"or\" : [ ] }"));
        var got = await store.RetrieveReportAsync("r1");
        Assert.Equal("{\"and\":[],\"or\":[]}", got!.Filters);
    }

    [Fact]
    public async Task InsertReport_EmptyNormalizeBy_GetsNone() {
        var (store, _) = Make();
        await store.InsertReportAsync(new ReportRow { Id = "r1", AccountId = "EI1", NormalizeBy = "" });
        var got = await store.RetrieveReportAsync("r1");
        Assert.Equal("none", got!.NormalizeBy);
    }

    [Fact]
    public async Task UpdateReport_ChangesFieldsAndUpdatedAt_KeepsCreatedAt() {
        long t = 100;
        long Now() => t;
        var fake = new FakeIndexedDb();
        var s = new IndexedDbReportStore(fake, Now);

        await s.InsertReportAsync(Report("r1", "EI1"));
        t = 500;
        await s.UpdateReportAsync(Report("r1", "EI1") with { Name = "updated" });

        var got = await s.RetrieveReportAsync("r1");
        Assert.Equal("updated", got!.Name);
        Assert.Equal(100, got.CreatedAt);
        Assert.Equal(500, got.UpdatedAt);
    }

    [Fact]
    public async Task DeleteReport_Removes() {
        var (store, _) = Make();
        await store.InsertReportAsync(Report("r1", "EI1"));
        await store.DeleteReportAsync("r1");
        Assert.Null(await store.RetrieveReportAsync("r1"));
    }

    [Fact]
    public async Task RetrieveAccountReports_OnlyAccountAndGlobal_OrderedBySortThenCreated() {
        long t = 0;
        var fake = new FakeIndexedDb();
        var store = new IndexedDbReportStore(fake, () => t);

        t = 10;
        await store.InsertReportAsync(Report("b", "EI1", sortOrder: 1));
        t = 20;
        await store.InsertReportAsync(Report("a", "EI1", sortOrder: 0));
        t = 5;
        await store.InsertReportAsync(Report("g", "__global__", sortOrder: 0));
        t = 30;
        await store.InsertReportAsync(Report("other", "EI2", sortOrder: 0));

        var rows = await store.RetrieveAccountReportsAsync("EI1");
        var ids = rows.Select(r => r.Id).ToArray();


        Assert.Equal(new[] { "g", "a", "b" }, ids);
    }

    [Fact]
    public async Task ReorderReports_SetsSortOrderToIndex() {
        var (store, _) = Make();
        await store.InsertReportAsync(Report("r1", "EI1", sortOrder: 9));
        await store.InsertReportAsync(Report("r2", "EI1", sortOrder: 9));
        await store.InsertReportAsync(Report("r3", "EI1", sortOrder: 9));

        await store.ReorderReportsAsync(new[] { "r3", "r1", "r2" });

        Assert.Equal(0, (await store.RetrieveReportAsync("r3"))!.SortOrder);
        Assert.Equal(1, (await store.RetrieveReportAsync("r1"))!.SortOrder);
        Assert.Equal(2, (await store.RetrieveReportAsync("r2"))!.SortOrder);
    }

    [Fact]
    public async Task InsertReportGroup_GeneratesIdWhenEmpty_AndRetrieveFindsIt() {
        var (store, _) = Make(now: 777);
        var id = await store.InsertReportGroupAsync(new ReportGroupRow { AccountId = "EI1", Name = "G", SortOrder = 2 });

        Assert.False(string.IsNullOrEmpty(id));
        var got = await store.RetrieveReportGroupAsync(id);
        Assert.NotNull(got);
        Assert.Equal(id, got!.Id);
        Assert.Equal("EI1", got.AccountId);
        Assert.Equal("G", got.Name);
        Assert.Equal(2, got.SortOrder);
        Assert.Equal(777, got.CreatedAt);
    }

    [Fact]
    public async Task InsertReportGroup_HonorsSuppliedId() {
        var (store, _) = Make();
        var id = await store.InsertReportGroupAsync(new ReportGroupRow { Id = "fixed", AccountId = "EI1", Name = "G" });
        Assert.Equal("fixed", id);
        Assert.NotNull(await store.RetrieveReportGroupAsync("fixed"));
    }

    [Fact]
    public async Task UpdateReportGroup_ChangesNameAndSort() {
        var (store, _) = Make();
        var id = await store.InsertReportGroupAsync(new ReportGroupRow { Id = "g1", AccountId = "EI1", Name = "old", SortOrder = 0 });
        await store.UpdateReportGroupAsync(new ReportGroupRow { Id = id, AccountId = "EI1", Name = "new", SortOrder = 5 });
        var got = await store.RetrieveReportGroupAsync(id);
        Assert.Equal("new", got!.Name);
        Assert.Equal(5, got.SortOrder);
    }

    [Fact]
    public async Task RetrieveAccountGroups_OnlyAccount_OrderedBySortThenCreated() {
        long t = 0;
        var fake = new FakeIndexedDb();
        var store = new IndexedDbReportStore(fake, () => t);

        t = 10;
        await store.InsertReportGroupAsync(new ReportGroupRow { Id = "b", AccountId = "EI1", SortOrder = 1 });
        t = 20;
        await store.InsertReportGroupAsync(new ReportGroupRow { Id = "a", AccountId = "EI1", SortOrder = 0 });
        t = 30;
        await store.InsertReportGroupAsync(new ReportGroupRow { Id = "other", AccountId = "EI2", SortOrder = 0 });

        var rows = await store.RetrieveAccountGroupsAsync("EI1");
        Assert.Equal(new[] { "a", "b" }, rows.Select(r => r.Id).ToArray());
    }

    [Fact]
    public async Task DeleteReportGroup_RemovesGroup_AndClearsMemberGroupId() {
        var (store, _) = Make();
        await store.InsertReportGroupAsync(new ReportGroupRow { Id = "g1", AccountId = "EI1", Name = "G" });
        await store.InsertReportAsync(Report("r1", "EI1") with { GroupId = "g1" });

        await store.DeleteReportGroupAsync("g1");

        Assert.Null(await store.RetrieveReportGroupAsync("g1"));
        Assert.Equal("", (await store.RetrieveReportAsync("r1"))!.GroupId);
    }

    [Fact]
    public async Task DeleteAllForAccount_RemovesOnlyThatAccountsReportsAndGroups() {
        var (store, _) = Make();
        await store.InsertReportGroupAsync(new ReportGroupRow { Id = "g1", AccountId = "EI1", Name = "G" });
        await store.InsertReportGroupAsync(new ReportGroupRow { Id = "g2", AccountId = "EI2", Name = "G2" });
        await store.InsertReportAsync(Report("r1", "EI1") with { GroupId = "g1" });
        await store.InsertReportAsync(Report("r2", "EI2") with { GroupId = "g2" });

        await store.DeleteAllForAccountAsync("EI1");

        Assert.Null(await store.RetrieveReportAsync("r1"));
        Assert.Null(await store.RetrieveReportGroupAsync("g1"));
        Assert.NotNull(await store.RetrieveReportAsync("r2"));
        Assert.NotNull(await store.RetrieveReportGroupAsync("g2"));
    }

    [Fact]
    public async Task SetReportGroup_AndRetrieveReportsByGroup() {
        var (store, _) = Make();
        await store.InsertReportGroupAsync(new ReportGroupRow { Id = "g1", AccountId = "EI1", Name = "G" });
        await store.InsertReportAsync(Report("r1", "EI1"));

        await store.SetReportGroupAsync("r1", "g1");

        var rows = await store.RetrieveReportsByGroupAsync("g1");
        Assert.Equal(new[] { "r1" }, rows.Select(r => r.Id).ToArray());
        Assert.Equal("g1", (await store.RetrieveReportAsync("r1"))!.GroupId);
    }

    [Fact]
    public async Task RetrieveReportsByGroup_OrderedBySortThenCreated() {
        long t = 0;
        var fake = new FakeIndexedDb();
        var store = new IndexedDbReportStore(fake, () => t);

        t = 10;
        await store.InsertReportAsync(Report("b", "EI1", sortOrder: 1) with { GroupId = "g1" });
        t = 20;
        await store.InsertReportAsync(Report("a", "EI1", sortOrder: 0) with { GroupId = "g1" });
        t = 30;
        await store.InsertReportAsync(Report("none", "EI1", sortOrder: 0) with { GroupId = "" });

        var rows = await store.RetrieveReportsByGroupAsync("g1");
        Assert.Equal(new[] { "a", "b" }, rows.Select(r => r.Id).ToArray());
    }
}
