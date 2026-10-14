using EggIdentity.Reports;
using EggLedger.Domain.Reports;
using EggLedger.Web.Data;

namespace EggLedger.Web.Tests.Data;

public sealed class IndexedDbReportRunnerTests {
    private const string Eid = "EI1";

    private sealed class NoWeights : IWeightData {
        public double CraftingWeight(long artifactId, long level) => 1;
        public IReadOnlyList<int> FamilyAfxIds(string familyId) => [];
    }

    private static IndexedDbReportRunner Runner(IIndexedDb db) =>
        new(new ReportSourceCache(IndexedDbReportSource.Loader(db, new FakeMissionStore()), TestTime.New()), new NoWeights());

    private static ReportQuery CountBy(string source, string dimension) => new(source) {
        GroupBy = [new GroupKey(dimension)],
        Sort = [new SortKey(MeasureRequest.RowCountKey, Descending: true)],
    };

    private static MissionRow Mission(string id, int ship, double start) => new() {
        PlayerId = Eid,
        MissionId = id,
        Ship = ship,
        StartTimestamp = start,
        ReturnTimestamp = start + 100,
        Capacity = 1,
        NominalCapacity = 1,
    };

    private static ArtifactDropRow Drop(string player, string mission, int index, int artifact, int rarity, int level) => new() {
        PlayerId = player,
        MissionId = mission,
        DropIndex = index,
        ArtifactId = artifact,
        Rarity = rarity,
        Level = level,
    };

    [Fact]
    public async Task RunReportAsync_AggregateByShip_RunsEndToEnd() {
        var db = new FakeIndexedDb();
        db.Seed("mission", Mission("a", ship: 9, start: 100));
        db.Seed("mission", Mission("b", ship: 9, start: 200));
        db.Seed("mission", Mission("c", ship: 3, start: 300));
        db.Seed("mission", Mission("other", ship: 9, start: 400) with { PlayerId = "EI2" });

        var cube = await Runner(db).RunReportAsync(CountBy(LedgerReportKeys.Missions, LedgerReportKeys.Ship), Eid);
        var series = ReportProjection.Series(cube, MeasureRequest.RowCountKey, LedgerReportSources.Missions);

        Assert.Equal([2d, 1d], series.Values);
        Assert.Equal("Henerprise", series.Labels[0]);
        Assert.Equal("BCR", series.Labels[1]);
    }

    [Fact]
    public async Task RunReportAsync_DropBasedByRarity_ReadsDropStore() {
        var db = new FakeIndexedDb();
        db.Seed("mission", Mission("a", ship: 9, start: 100));
        db.Seed("artifact_drops", Drop(Eid, "a", 0, 12, 3, 2));
        db.Seed("artifact_drops", Drop(Eid, "a", 1, 13, 3, 1));
        db.Seed("artifact_drops", Drop(Eid, "a", 2, 14, 1, 0));
        db.Seed("artifact_drops", Drop("EI2", "z", 0, 99, 3, 0));

        var cube = await Runner(db).RunReportAsync(CountBy(LedgerReportKeys.Artifacts, LedgerReportKeys.Rarity), Eid);
        var series = ReportProjection.Series(cube, MeasureRequest.RowCountKey, LedgerReportSources.Artifacts);

        Assert.Equal([2d, 1d], series.Values);
        Assert.Equal("Legendary", series.Labels[0]);
        Assert.Equal("Rare", series.Labels[1]);
    }

    [Fact]
    public async Task RunReportAsync_DropReport_ReadsDropsByIndexNotWholeStore() {
        var db = new CountingIndexedDb();
        db.Seed("mission", Mission("a", ship: 9, start: 100));
        db.Seed("artifact_drops", Drop(Eid, "a", 0, 12, 3, 0));
        db.Seed("artifact_drops", Drop("EI2", "z", 0, 99, 3, 0));

        var cube = await Runner(db).RunReportAsync(CountBy(LedgerReportKeys.Artifacts, LedgerReportKeys.Rarity), Eid);

        Assert.Contains(("artifact_drops", "player_id"), db.IndexQueries);
        Assert.DoesNotContain("artifact_drops", db.WholeStoreReads);
        Assert.Equal([1d], ReportProjection.Series(cube, MeasureRequest.RowCountKey).Values);
    }

    [Fact]
    public async Task RunReportAsync_EmptyWhenNoRows() {
        var cube = await Runner(new FakeIndexedDb()).RunReportAsync(CountBy(LedgerReportKeys.Missions, LedgerReportKeys.Ship), Eid);

        Assert.Empty(cube.Rows);
    }

    private sealed class CountingIndexedDb : IIndexedDb {
        private readonly FakeIndexedDb _inner = new();
        public List<(string Store, string Index)> IndexQueries { get; } = [];
        public List<string> WholeStoreReads { get; } = [];

        public void Seed(string store, object row) => _inner.Seed(store, row);

        public ValueTask<T[]> GetAllByIndexAsync<T>(string store, string index, object value) {
            IndexQueries.Add((store, index));
            return _inner.GetAllByIndexAsync<T>(store, index, value);
        }

        public ValueTask<T[]> GetAllByIndexProjectedAsync<T>(string store, string index, object value) {
            IndexQueries.Add((store, index));
            return _inner.GetAllByIndexProjectedAsync<T>(store, index, value);
        }

        public ValueTask<T[]> GetAllAsync<T>(string store) {
            WholeStoreReads.Add(store);
            return _inner.GetAllAsync<T>(store);
        }

        public ValueTask<T?> GetAsync<T>(string store, object key) => _inner.GetAsync<T>(store, key);
        public ValueTask PutAsync(string store, object value) => _inner.PutAsync(store, value);
        public ValueTask<int> PutManyAsync(string store, IEnumerable<object> values) => _inner.PutManyAsync(store, values);
        public ValueTask DeleteAsync(string store, object key) => _inner.DeleteAsync(store, key);
        public ValueTask ClearAsync(string store) => _inner.ClearAsync(store);
        public ValueTask<int> CountAsync(string store) => _inner.CountAsync(store);
    }
}
