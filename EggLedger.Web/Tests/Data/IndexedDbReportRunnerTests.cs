using EggLedger.Domain.Reports;
using EggLedger.Web.Data;

namespace EggLedger.Web.Tests.Data;

public sealed class IndexedDbReportRunnerTests {
    private const string Eid = "EI1";

    private sealed class NoWeights : IWeightData {
        public double CraftingWeight(long artifactId, long level) => 1;
        public IReadOnlyList<int> FamilyAfxIds(string familyId) => Array.Empty<int>();
    }

    private static IndexedDbReportRunner Runner(IIndexedDb db) =>
        new(new ReportSourceCache(IndexedDbReportSource.Loader(db, new FakeMissionStore())), new NoWeights());

    private static MissionRow Mission(string id, int ship, double start) => new() {
        PlayerId = Eid,
        MissionId = id,
        Ship = ship,
        StartTimestamp = start,
        ReturnTimestamp = start + 100,
        Capacity = 1,
        NominalCapacity = 1,
    };

    [Fact]
    public async Task RunReportAsync_AggregateByShip_RunsEndToEnd() {
        var db = new FakeIndexedDb();
        db.Seed("mission", Mission("a", ship: 9, start: 100));
        db.Seed("mission", Mission("b", ship: 9, start: 200));
        db.Seed("mission", Mission("c", ship: 3, start: 300));
        db.Seed("mission", Mission("other", ship: 9, start: 400) with { PlayerId = "EI2" });

        var sut = Runner(db);
        var def = new ReportDefinition {
            Mode = "aggregate",
            GroupBy = "ship_type",
            Subject = "missions",
            AccountId = Eid,
        };

        var result = await sut.RunReportAsync(def, Eid);

        Assert.False(result.Is2D);
        Assert.Equal([2, 1], result.Values);
        Assert.Equal("Henerprise", result.Labels[0]);
        Assert.Equal("BCR", result.Labels[1]);
    }

    [Fact]
    public async Task RunReportAsync_DropBasedByRarity_ReadsDropStore() {
        var db = new FakeIndexedDb();
        db.Seed("mission", Mission("a", ship: 9, start: 100));
        db.Seed("artifact_drops", new ArtifactDropRow {
            PlayerId = Eid,
            MissionId = "a",
            DropIndex = 0,
            ArtifactId = 12,
            Rarity = 3,
            Level = 2,
        });
        db.Seed("artifact_drops", new ArtifactDropRow {
            PlayerId = Eid,
            MissionId = "a",
            DropIndex = 1,
            ArtifactId = 13,
            Rarity = 3,
            Level = 1,
        });
        db.Seed("artifact_drops", new ArtifactDropRow {
            PlayerId = Eid,
            MissionId = "a",
            DropIndex = 2,
            ArtifactId = 14,
            Rarity = 1,
            Level = 0,
        });

        db.Seed("artifact_drops", new ArtifactDropRow {
            PlayerId = "EI2",
            MissionId = "z",
            DropIndex = 0,
            ArtifactId = 99,
            Rarity = 3,
            Level = 0,
        });

        var sut = Runner(db);
        var def = new ReportDefinition {
            Mode = "aggregate",
            GroupBy = "rarity",
            Subject = "artifacts",
            AccountId = Eid,
        };

        var result = await sut.RunReportAsync(def, Eid);


        Assert.Equal([2, 1], result.Values);
        Assert.Equal("Legendary", result.Labels[0]);
        Assert.Equal("Rare", result.Labels[1]);
    }

    [Fact]
    public async Task RunReportAsync_DropReport_ReadsDropsByIndexNotWholeStore() {
        var db = new CountingIndexedDb();
        db.Seed("mission", Mission("a", ship: 9, start: 100));
        db.Seed("artifact_drops", new ArtifactDropRow {
            PlayerId = Eid,
            MissionId = "a",
            DropIndex = 0,
            ArtifactId = 12,
            Rarity = 3,
            Level = 0,
        });
        db.Seed("artifact_drops", new ArtifactDropRow {
            PlayerId = "EI2",
            MissionId = "z",
            DropIndex = 0,
            ArtifactId = 99,
            Rarity = 3,
            Level = 0,
        });

        var sut = Runner(db);
        var def = new ReportDefinition {
            Mode = "aggregate",
            GroupBy = "rarity",
            Subject = "artifacts",
            AccountId = Eid,
        };

        var result = await sut.RunReportAsync(def, Eid);

        Assert.Contains(("artifact_drops", "player_id"), db.IndexQueries);
        Assert.DoesNotContain("artifact_drops", db.WholeStoreReads);
        Assert.Equal([1], result.Values);
    }

    [Fact]
    public async Task RunReportAsync_EmptyWhenNoRows() {
        var db = new FakeIndexedDb();
        var sut = Runner(db);
        var def = new ReportDefinition {
            Mode = "aggregate",
            GroupBy = "ship_type",
            Subject = "missions",
            AccountId = Eid,
        };

        var result = await sut.RunReportAsync(def, Eid);

        Assert.Empty(result.Values);
        Assert.Empty(result.Labels);
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
