using EggLedger.Domain.MissionPacking;
using EggLedger.Domain.MissionQuery;
using EggLedger.Domain.Reports;
using EggLedger.Web.Data;
using EggLedger.Web.State;

namespace EggLedger.Web.Tests.Data;

public sealed class ReportSourceCacheTests {
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    private sealed class NoWeights : IWeightData {
        public double CraftingWeight(long artifactId, long level) => 1;
        public IReadOnlyList<int> FamilyAfxIds(string familyId) => [];
    }

    private static ReportSource SourceFor(string accountId) => new(
        [new MissionRowData { PlayerId = accountId, MissionId = accountId + "-m1" }],
        [new ArtifactDropRowData { PlayerId = accountId, MissionId = accountId + "-m1" }],
        [new FuelRowData { PlayerId = accountId, MissionId = accountId + "-m1" }]);

    private static Task<ReportSource> SourceOf(string accountId) => Task.FromResult(SourceFor(accountId));

    private static ReportSourceCache NewCache(
        Func<string, Task<ReportSource>>? loader = null,
        TimeSpan? ttl = null,
        Func<DateTime>? clock = null) =>
        new(loader ?? SourceOf, ttl ?? Ttl, clock);

    private static LedgerDataHub NewHub() =>
        new(_ => Task.FromResult<IReadOnlyList<DatabaseMission>?>(null),
            _ => Task.FromResult<Dictionary<string, List<MissionDrop>>?>(null),
            Ttl);

    [Fact]
    public async Task SecondGet_ForTheSameAccount_DoesNotRescan() {
        var calls = 0;
        var cache = NewCache(id => {
            calls++;
            return SourceOf(id);
        });

        var first = await cache.GetAsync("a");
        var second = await cache.GetAsync("a");

        Assert.Same(first, second);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Invalidate_ForcesARescan_OfThatAccountOnly() {
        var calls = new Dictionary<string, int>(StringComparer.Ordinal);
        var cache = NewCache(id => {
            calls[id] = calls.GetValueOrDefault(id) + 1;
            return SourceOf(id);
        });

        await cache.GetAsync("a");
        await cache.GetAsync("b");
        cache.Invalidate("a");
        await cache.GetAsync("a");
        await cache.GetAsync("b");

        Assert.Equal(2, calls["a"]);
        Assert.Equal(1, calls["b"]);
    }

    [Fact]
    public async Task Accounts_AreCachedIndependently_AcrossSwitching() {
        var calls = new Dictionary<string, int>(StringComparer.Ordinal);
        var cache = NewCache(id => {
            calls[id] = calls.GetValueOrDefault(id) + 1;
            return SourceOf(id);
        });

        var firstA = await cache.GetAsync("a");
        var firstB = await cache.GetAsync("b");
        var secondA = await cache.GetAsync("a");

        Assert.Same(firstA, secondA);
        Assert.NotSame(firstA, firstB);
        Assert.Equal(1, calls["a"]);
        Assert.Equal(1, calls["b"]);
    }

    [Fact]
    public async Task FourthAccount_EvictsLeastRecentlyUsedAccount() {
        var calls = new Dictionary<string, int>(StringComparer.Ordinal);
        var cache = NewCache(id => {
            calls[id] = calls.GetValueOrDefault(id) + 1;
            return SourceOf(id);
        });

        await cache.GetAsync("a");
        await cache.GetAsync("b");
        await cache.GetAsync("c");
        await cache.GetAsync("a");
        await cache.GetAsync("d");
        await cache.GetAsync("a");
        await cache.GetAsync("b");

        Assert.Equal(1, calls["a"]);
        Assert.Equal(1, calls["c"]);
        Assert.Equal(1, calls["d"]);
        Assert.Equal(2, calls["b"]);
    }

    [Fact]
    public async Task ConcurrentRequests_ShareASingleScan() {
        var gate = new TaskCompletionSource<ReportSource>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var cache = NewCache(_ => {
            calls++;
            return gate.Task;
        });

        var first = cache.GetAsync("a");
        var second = cache.GetAsync("a");
        var third = cache.GetAsync("a");
        gate.SetResult(SourceFor("a"));
        var results = await Task.WhenAll(first, second, third);

        Assert.Equal(1, calls);
        Assert.Same(results[0], results[1]);
        Assert.Same(results[1], results[2]);
    }

    [Fact]
    public async Task EmptyScan_IsAValidCachedResult() {
        var calls = 0;
        var cache = NewCache(_ => {
            calls++;
            return Task.FromResult(ReportSource.Empty);
        });

        var first = await cache.GetAsync("a");
        var second = await cache.GetAsync("a");

        Assert.Empty(first.Missions);
        Assert.Empty(first.Drops);
        Assert.Same(first, second);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task FailedScan_DoesNotPoisonTheSlot() {
        var calls = 0;
        var cache = NewCache(id => {
            calls++;
            return calls == 1
                ? Task.FromException<ReportSource>(new InvalidOperationException("boom"))
                : SourceOf(id);
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.GetAsync("a"));
        var source = await cache.GetAsync("a");

        Assert.Single(source.Missions);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Source_RescansOnlyAfterTtlExpires() {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var calls = 0;
        var cache = NewCache(id => {
            calls++;
            return SourceOf(id);
        }, clock: () => now);

        await cache.GetAsync("a");
        now = now.AddMinutes(4);
        await cache.GetAsync("a");
        Assert.Equal(1, calls);

        now = now.AddMinutes(2);
        await cache.GetAsync("a");

        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task HubInvalidation_DropsTheCachedSource() {
        var calls = new Dictionary<string, int>(StringComparer.Ordinal);
        var cache = NewCache(id => {
            calls[id] = calls.GetValueOrDefault(id) + 1;
            return SourceOf(id);
        });
        var hub = NewHub();
        cache.AttachHub(hub);

        await cache.GetAsync("a");
        await cache.GetAsync("b");
        hub.Invalidate("a");
        await cache.GetAsync("a");
        await cache.GetAsync("b");

        Assert.Equal(2, calls["a"]);
        Assert.Equal(1, calls["b"]);
    }

    [Fact]
    public async Task Dispose_StopsListeningToTheHub() {
        var calls = 0;
        var cache = NewCache(id => {
            calls++;
            return SourceOf(id);
        });
        var hub = NewHub();
        cache.AttachHub(hub);

        await cache.GetAsync("a");
        cache.Dispose();
        hub.Invalidate("a");
        await cache.GetAsync("a");

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task IndexedDbLoader_ReadsBothStoresForTheAccount_AndMapsRows() {
        var db = new FakeIndexedDb();
        db.Seed(IndexedDbStores.Mission, new MissionRow {
            PlayerId = "EI1",
            MissionId = "m1",
            Ship = 9,
            DurationType = 2,
            Level = 4,
            Target = 5,
            MissionType = 1,
            StartTimestamp = 100.5,
            ReturnTimestamp = 200.5,
            Capacity = 30,
            NominalCapacity = 20,
            IsDubCap = true,
            IsBuggedCap = true,
        });
        db.Seed(IndexedDbStores.Mission, new MissionRow { PlayerId = "EI2", MissionId = "m2" });
        db.Seed(IndexedDbStores.ArtifactDrops, new ArtifactDropRow {
            PlayerId = "EI1",
            MissionId = "m1",
            DropIndex = 3,
            ArtifactId = 12,
            SpecType = "artifact",
            Level = 2,
            Rarity = 3,
            Quality = 4.5,
        });
        db.Seed(IndexedDbStores.ArtifactDrops, new ArtifactDropRow { PlayerId = "EI2", MissionId = "m2" });

        var source = await IndexedDbReportSource.LoadAsync(db, "EI1");

        var mission = Assert.Single(source.Missions);
        Assert.Equal("EI1", mission.PlayerId);
        Assert.Equal("m1", mission.MissionId);
        Assert.Equal(9, mission.Ship);
        Assert.Equal(2, mission.DurationType);
        Assert.Equal(4, mission.Level);
        Assert.Equal(5, mission.Target);
        Assert.Equal(1, mission.MissionType);
        Assert.Equal(100, mission.StartTimestamp);
        Assert.Equal(200, mission.ReturnTimestamp);
        Assert.Equal(30, mission.Capacity);
        Assert.Equal(20, mission.NominalCapacity);
        Assert.True(mission.IsDubCap);
        Assert.True(mission.IsBuggedCap);

        var drop = Assert.Single(source.Drops);
        Assert.Equal("EI1", drop.PlayerId);
        Assert.Equal("m1", drop.MissionId);
        Assert.Equal(3, drop.DropIndex);
        Assert.Equal(12, drop.ArtifactId);
        Assert.Equal("artifact", drop.SpecType);
        Assert.Equal(2, drop.Level);
        Assert.Equal(3, drop.Rarity);
        Assert.Equal(4.5, drop.Quality);
    }

    [Fact]
    public async Task RepeatedReportRuns_ForOneAccount_ScanOnce() {
        var db = new FakeIndexedDb();
        db.Seed(IndexedDbStores.Mission, new MissionRow {
            PlayerId = "EI1",
            MissionId = "m1",
            Ship = 9,
            StartTimestamp = 100,
            ReturnTimestamp = 200,
            Capacity = 1,
            NominalCapacity = 1,
        });

        var scans = 0;
        var cache = NewCache(id => {
            scans++;
            return IndexedDbReportSource.LoadAsync(db, id);
        });
        var runner = new IndexedDbReportRunner(cache, new NoWeights());
        var def = new ReportDefinition {
            Mode = "aggregate",
            GroupBy = "ship_type",
            Subject = "missions",
            AccountId = "EI1",
        };

        var first = await runner.RunReportAsync(def, "EI1");
        var second = await runner.RunReportAsync(def, "EI1");

        Assert.Equal(1, scans);
        Assert.Equal([1], first.Values);
        Assert.Equal([1], second.Values);
    }

    [Fact]
    public async Task IndexedDbLoader_WithMissionStore_BackfillsFilterColsBeforeReadingRows() {
        var db = new FakeIndexedDb();
        db.Seed(IndexedDbStores.Mission, new MissionRow { PlayerId = "EI1", MissionId = "m1", Ship = -1 });
        var missionStore = new FakeMissionStore();

        var source = await IndexedDbReportSource.LoadAsync(db, missionStore, "EI1");

        Assert.Equal(["EI1"], missionStore.BackfillsEnsured);
        Assert.Single(source.Missions);
    }
}
