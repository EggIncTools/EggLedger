using EggLedger.Domain.MissionPacking;
using EggLedger.Domain.Reports;
using EggLedger.Web.State;
using EggLedger.Web.Tests.Data;

namespace EggLedger.Web.Tests.State;

public sealed class LedgerDataHubTests {
    private static LedgerDataHub NewHub(
        Func<string, Task<LedgerReportData>>? loader = null,
        TimeProvider? clock = null,
        Func<string, Task<IReadOnlyList<DatabaseMission>?>>? inFlight = null) =>
        TestHub.New(loader, clock: clock, inFlight: inFlight);

    [Fact]
    public async Task Missions_StayCachedPerAccount_AcrossAccountSwitching() {
        var calls = new Dictionary<string, int>(StringComparer.Ordinal);
        var hub = NewHub(id => {
            calls[id] = calls.GetValueOrDefault(id) + 1;
            return TestHub.SourceOf(id);
        });

        var firstA = await hub.GetMissionsAsync("a");
        await hub.GetMissionsAsync("b");
        var secondA = await hub.GetMissionsAsync("a");

        Assert.Same(firstA, secondA);
        Assert.Equal("a-m1", Assert.Single(firstA!).MissiondId);
        Assert.Equal(1, calls["a"]);
        Assert.Equal(1, calls["b"]);
    }

    [Fact]
    public async Task MissionsAndDrops_ShareOneLoadOfTheDataset() {
        var calls = 0;
        var hub = NewHub(id => {
            calls++;
            return TestHub.SourceOf(id);
        });

        var missions = await hub.GetMissionsAsync("a");
        var drops = await hub.GetDropsAsync("a");
        var lifetime = await hub.GetLifetimeAggregateAsync("a");

        Assert.Equal(1, calls);
        Assert.Single(missions!);
        Assert.Single(drops!["a-m1"]);
        Assert.Equal(1, lifetime!.MissionCount);
    }

    [Fact]
    public async Task ConcurrentMissionRequests_ShareASingleLoad() {
        var gate = new TaskCompletionSource<LedgerReportData>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var hub = NewHub(_ => {
            calls++;
            return gate.Task;
        });

        var first = hub.GetMissionsAsync("a");
        var second = hub.GetMissionsAsync("a");
        gate.SetResult(TestHub.SourceFor("a"));
        var results = await Task.WhenAll(first, second);

        Assert.Equal(1, calls);
        Assert.Same(results[0], results[1]);
    }

    [Fact]
    public async Task Invalidate_ClearsOnlyThatAccount_AndRaisesAccountInvalidated() {
        var calls = new Dictionary<string, int>(StringComparer.Ordinal);
        var hub = NewHub(id => {
            calls[id] = calls.GetValueOrDefault(id) + 1;
            return TestHub.SourceOf(id);
        });
        var invalidated = new List<string>();
        hub.AccountInvalidated += invalidated.Add;

        await hub.GetMissionsAsync("a");
        await hub.GetMissionsAsync("b");
        hub.Invalidate("a");
        await hub.GetMissionsAsync("a");
        await hub.GetMissionsAsync("b");

        Assert.Equal(2, calls["a"]);
        Assert.Equal(1, calls["b"]);
        Assert.Equal("a", Assert.Single(invalidated));
    }

    [Fact]
    public async Task Missions_ReloadOnlyAfterTtlExpires() {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var calls = 0;
        var hub = NewHub(id => {
            calls++;
            return TestHub.SourceOf(id);
        }, clock: clock);

        await hub.GetMissionsAsync("a");
        clock.Advance(TimeSpan.FromMinutes(4));
        await hub.GetMissionsAsync("a");
        Assert.Equal(1, calls);

        clock.Advance(TimeSpan.FromMinutes(2));
        await hub.GetMissionsAsync("a");

        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task FailedLoad_DoesNotPoisonTheSlot() {
        var calls = 0;
        var hub = NewHub(id => {
            calls++;
            return calls == 1
                ? Task.FromException<LedgerReportData>(new InvalidOperationException("boom"))
                : TestHub.SourceOf(id);
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => hub.GetMissionsAsync("a"));
        var missions = await hub.GetMissionsAsync("a");

        Assert.NotNull(missions);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task UnbackfilledMissions_AreExcludedFromTheMissionList() {
        var hub = NewHub(_ => Task.FromResult(new LedgerReportData(
            [
                new MissionRowData { MissionId = "m1", Ship = -1 },
                new MissionRowData { MissionId = "m2", StartTimestamp = 5, ReturnTimestamp = 6 },
            ],
            [],
            [])));

        var missions = await hub.GetMissionsAsync("a");

        Assert.Equal("m2", Assert.Single(missions!).MissiondId);
    }

    [Fact]
    public async Task InFlight_IsCachedPerAccount_UntilInvalidated() {
        var calls = new Dictionary<string, int>(StringComparer.Ordinal);
        var hub = NewHub(inFlight: id => {
            calls[id] = calls.GetValueOrDefault(id) + 1;
            return Task.FromResult<IReadOnlyList<DatabaseMission>?>([new DatabaseMission { MissiondId = id + "-f1" }]);
        });

        var firstA = await hub.GetInFlightAsync("a");
        await hub.GetInFlightAsync("b");
        var secondA = await hub.GetInFlightAsync("a");

        Assert.Same(firstA, secondA);
        Assert.Equal(1, calls["a"]);
        Assert.Equal(1, calls["b"]);

        hub.Invalidate("a");
        await hub.GetInFlightAsync("a");

        Assert.Equal(2, calls["a"]);
    }

    [Fact]
    public async Task ConcurrentInFlightRequests_ShareASingleLoad() {
        var gate = new TaskCompletionSource<IReadOnlyList<DatabaseMission>?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var hub = NewHub(inFlight: _ => {
            calls++;
            return gate.Task;
        });

        var first = hub.GetInFlightAsync("a");
        var second = hub.GetInFlightAsync("a");
        gate.SetResult([new DatabaseMission { MissiondId = "f" }]);
        var results = await Task.WhenAll(first, second);

        Assert.Equal(1, calls);
        Assert.Same(results[0], results[1]);
    }

    [Fact]
    public async Task FailedInFlightLoad_DoesNotPoisonTheSlot() {
        var calls = 0;
        var hub = NewHub(inFlight: id => {
            calls++;
            return calls == 1
                ? Task.FromException<IReadOnlyList<DatabaseMission>?>(new InvalidOperationException("boom"))
                : Task.FromResult<IReadOnlyList<DatabaseMission>?>([new DatabaseMission { MissiondId = id + "-f1" }]);
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => hub.GetInFlightAsync("a"));
        var missions = await hub.GetInFlightAsync("a");

        Assert.NotNull(missions);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task InFlight_IsEmpty_WhenNoLoaderIsSupplied() {
        var hub = NewHub();

        Assert.Empty((await hub.GetInFlightAsync("a"))!);
    }

    [Fact]
    public async Task FilterMatches_AreCachedPerHash_AndClearedOnInvalidate() {
        var hub = NewHub();
        var computes = 0;

        Task<IReadOnlySet<string>> ComputeAsync() {
            computes++;
            return Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(StringComparer.Ordinal) { "m1" });
        }

        var first = await hub.GetFilterMatchesAsync("a", "hash-1", ComputeAsync);
        var second = await hub.GetFilterMatchesAsync("a", "hash-1", ComputeAsync);
        await hub.GetFilterMatchesAsync("a", "hash-2", ComputeAsync);
        Assert.Equal(2, computes);

        hub.Invalidate("a");
        await hub.GetFilterMatchesAsync("a", "hash-1", ComputeAsync);

        Assert.Same(first, second);
        Assert.Equal(3, computes);
    }

    [Fact]
    public async Task ConcurrentFilterMatchRequests_ShareASingleCompute() {
        var hub = NewHub();
        var gate = new TaskCompletionSource<IReadOnlySet<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var computes = 0;

        Task<IReadOnlySet<string>> ComputeAsync() {
            computes++;
            return gate.Task;
        }

        var first = hub.GetFilterMatchesAsync("a", "hash-1", ComputeAsync);
        var second = hub.GetFilterMatchesAsync("a", "hash-1", ComputeAsync);
        var third = hub.GetFilterMatchesAsync("a", "hash-1", ComputeAsync);
        gate.SetResult(new HashSet<string>(StringComparer.Ordinal) { "m1" });
        var results = await Task.WhenAll(first, second, third);

        Assert.Equal(1, computes);
        Assert.Same(results[0], results[1]);
        Assert.Same(results[1], results[2]);
    }

    [Fact]
    public async Task FaultedFilterMatchCompute_DoesNotStick() {
        var hub = NewHub();
        var computes = 0;

        Task<IReadOnlySet<string>> ComputeAsync() {
            computes++;
            return computes == 1
                ? Task.FromException<IReadOnlySet<string>>(new InvalidOperationException("boom"))
                : Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(StringComparer.Ordinal) { "m1" });
        }

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => hub.GetFilterMatchesAsync("a", "hash-1", ComputeAsync));
        var matches = await hub.GetFilterMatchesAsync("a", "hash-1", ComputeAsync);

        Assert.Equal(2, computes);
        Assert.Single(matches);
    }

    [Fact]
    public async Task FilterMatches_AreKeptPerAccount() {
        var hub = NewHub();
        var computes = 0;

        Task<IReadOnlySet<string>> ComputeAsync() {
            computes++;
            return Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(StringComparer.Ordinal));
        }

        await hub.GetFilterMatchesAsync("a", "hash-1", ComputeAsync);
        await hub.GetFilterMatchesAsync("b", "hash-1", ComputeAsync);
        await hub.GetFilterMatchesAsync("a", "hash-1", ComputeAsync);

        Assert.Equal(2, computes);
    }
}
