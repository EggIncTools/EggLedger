using EggLedger.Web.Data;
using Xunit;

namespace EggLedger.Web.Tests.Data;

public sealed class IndexedDbPinnedReportStoreTests {
    [Fact]
    public async Task InsertThenRetrieve_ReturnsRowsForViewOnly() {
        var db = new FakeIndexedDb();
        var store = new IndexedDbPinnedReportStore(db, () => 1000);

        await store.InsertAsync(new PinnedReportRow { AccountId = "a1", View = PinnedReportViews.Lifetime, Kind = PinnedReportKinds.Template, RefId = "tmpl_x" });
        await store.InsertAsync(new PinnedReportRow { AccountId = "a1", View = PinnedReportViews.Missions, Kind = PinnedReportKinds.Template, RefId = "tmpl_y" });

        var lifetime = await store.RetrieveAsync("a1", PinnedReportViews.Lifetime);
        Assert.Single(lifetime);
        Assert.Equal("tmpl_x", lifetime[0].RefId);
    }

    [Fact]
    public async Task Reorder_UpdatesSortOrder() {
        var db = new FakeIndexedDb();
        var store = new IndexedDbPinnedReportStore(db, () => 1000);
        await store.InsertAsync(new PinnedReportRow { Id = "r1", AccountId = "a1", View = "lifetime", Kind = "template", RefId = "x" });
        await store.InsertAsync(new PinnedReportRow { Id = "r2", AccountId = "a1", View = "lifetime", Kind = "template", RefId = "y" });

        await store.ReorderAsync("a1", "lifetime", ["r2", "r1"]);

        var rows = await store.RetrieveAsync("a1", "lifetime");
        Assert.Equal("r2", rows[0].Id);
        Assert.Equal("r1", rows[1].Id);
    }

    [Fact]
    public async Task DeleteAsync_RemovesRow() {
        var db = new FakeIndexedDb();
        var store = new IndexedDbPinnedReportStore(db, () => 1000);
        await store.InsertAsync(new PinnedReportRow { Id = "r1", AccountId = "a1", View = "lifetime", Kind = "user", RefId = "rep_1" });

        await store.DeleteAsync("r1");

        var rows = await store.RetrieveAsync("a1", "lifetime");
        Assert.Empty(rows);
    }
}
