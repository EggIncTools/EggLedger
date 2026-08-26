using EggLedger.Domain.MissionPacking;
using EggLedger.Domain.MissionQuery;
using EggLedger.Web.State;

namespace EggLedger.Web.Tests.State;

public sealed class LedgerDataHubTests {
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    private static IReadOnlyList<DatabaseMission> MissionsFor(string accountId) =>
        [new DatabaseMission { MissiondId = accountId + "-m1" }];

    private static Task<IReadOnlyList<DatabaseMission>?> MissionsOf(string accountId) =>
        Task.FromResult<IReadOnlyList<DatabaseMission>?>(MissionsFor(accountId));

    private static LedgerDataHub NewHub(
        Func<string, Task<IReadOnlyList<DatabaseMission>?>>? missions = null,
        Func<string, Task<Dictionary<string, List<MissionDrop>>?>>? drops = null,
        TimeSpan? ttl = null,
        Func<DateTime>? clock = null,
        Func<string, Task<IReadOnlyList<DatabaseMission>?>>? inFlight = null) =>
        new(missions ?? MissionsOf,
            drops ?? (_ => Task.FromResult<Dictionary<string, List<MissionDrop>>?>(null)),
            ttl ?? Ttl,
            clock,
            inFlight);

    [Fact]
    public async Task Missions_StayCachedPerAccount_AcrossAccountSwitching() {
        var calls = new Dictionary<string, int>(StringComparer.Ordinal);
        var hub = NewHub(id => {
            calls[id] = calls.GetValueOrDefault(id) + 1;
            return MissionsOf(id);
        });

        var firstA = await hub.GetMissionsAsync("a");
        await hub.GetMissionsAsync("b");
        var secondA = await hub.GetMissionsAsync("a");

        Assert.Same(firstA, secondA);
        Assert.Equal(1, calls["a"]);
        Assert.Equal(1, calls["b"]);
    }

    [Fact]
    public async Task FourthAccount_EvictsLeastRecentlyUsedAccount() {
        var calls = new Dictionary<string, int>(StringComparer.Ordinal);
        var hub = NewHub(id => {
            calls[id] = calls.GetValueOrDefault(id) + 1;
            return MissionsOf(id);
        });

        await hub.GetMissionsAsync("a");
        await hub.GetMissionsAsync("b");
        await hub.GetMissionsAsync("c");
        await hub.GetMissionsAsync("a");
        await hub.GetMissionsAsync("d");
        await hub.GetMissionsAsync("a");
        await hub.GetMissionsAsync("b");

        Assert.Equal(1, calls["a"]);
        Assert.Equal(1, calls["c"]);
        Assert.Equal(1, calls["d"]);
        Assert.Equal(2, calls["b"]);
    }

    [Fact]
    public async Task ConcurrentMissionRequests_ShareASingleLoad() {
        var gate = new TaskCompletionSource<IReadOnlyList<DatabaseMission>?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var hub = NewHub(_ => {
            calls++;
            return gate.Task;
        });

        var first = hub.GetMissionsAsync("a");
        var second = hub.GetMissionsAsync("a");
        gate.SetResult(MissionsFor("a"));
        var results = await Task.WhenAll(first, second);

        Assert.Equal(1, calls);
        Assert.Same(results[0], results[1]);
    }

    [Fact]
    public async Task Invalidate_ClearsOnlyThatAccount_AndRaisesAccountInvalidated() {
        var calls = new Dictionary<string, int>(StringComparer.Ordinal);
        var hub = NewHub(id => {
            calls[id] = calls.GetValueOrDefault(id) + 1;
            return MissionsOf(id);
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
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var calls = 0;
        var hub = NewHub(id => {
            calls++;
            return MissionsOf(id);
        }, clock: () => now);

        await hub.GetMissionsAsync("a");
        now = now.AddMinutes(4);
        await hub.GetMissionsAsync("a");
        Assert.Equal(1, calls);

        now = now.AddMinutes(2);
        await hub.GetMissionsAsync("a");

        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task NullResult_IsNotCachedAsAHit() {
        var calls = 0;
        var hub = NewHub(_ => {
            calls++;
            return Task.FromResult<IReadOnlyList<DatabaseMission>?>(null);
        });

        Assert.Null(await hub.GetMissionsAsync("a"));
        Assert.Null(await hub.GetMissionsAsync("a"));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task FailedLoad_DoesNotPoisonTheSlot() {
        var calls = 0;
        var hub = NewHub(id => {
            calls++;
            return calls == 1
                ? Task.FromException<IReadOnlyList<DatabaseMission>?>(new InvalidOperationException("boom"))
                : MissionsOf(id);
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => hub.GetMissionsAsync("a"));
        var missions = await hub.GetMissionsAsync("a");

        Assert.NotNull(missions);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task LifetimeAggregate_ComposesOverTheCachedDropsSlot() {
        var dropCalls = 0;
        var hub = NewHub(drops: _ => {
            dropCalls++;
            return Task.FromResult<Dictionary<string, List<MissionDrop>>?>(new Dictionary<string, List<MissionDrop>> {
                ["m1"] = [],
                ["m2"] = [],
            });
        });

        await hub.GetDropsAsync("a");
        var first = await hub.GetLifetimeAggregateAsync("a");
        var second = await hub.GetLifetimeAggregateAsync("a");

        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.Equal(2, first.MissionCount);
        Assert.Equal(1, dropCalls);
    }

    [Fact]
    public async Task LifetimeAggregate_IsNull_WhenDropsAreMissing() {
        var hub = NewHub();

        Assert.Null(await hub.GetLifetimeAggregateAsync("a"));
    }

    [Fact]
    public async Task InFlight_IsCachedPerAccount_UntilTtlExpires() {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var calls = new Dictionary<string, int>(StringComparer.Ordinal);
        var hub = NewHub(clock: () => now, inFlight: id => {
            calls[id] = calls.GetValueOrDefault(id) + 1;
            return Task.FromResult<IReadOnlyList<DatabaseMission>?>([new DatabaseMission { MissiondId = id + "-f1" }]);
        });

        var firstA = await hub.GetInFlightAsync("a");
        await hub.GetInFlightAsync("b");
        var secondA = await hub.GetInFlightAsync("a");

        Assert.Same(firstA, secondA);
        Assert.Equal(1, calls["a"]);
        Assert.Equal(1, calls["b"]);

        now = now.AddMinutes(6);
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
        gate.SetResult(MissionsFor("a"));
        var results = await Task.WhenAll(first, second);

        Assert.Equal(1, calls);
        Assert.Same(results[0], results[1]);
    }

    [Fact]
    public async Task Invalidate_DropsTheInFlightSlotToo() {
        var calls = 0;
        var hub = NewHub(inFlight: id => {
            calls++;
            return Task.FromResult<IReadOnlyList<DatabaseMission>?>([new DatabaseMission { MissiondId = id + "-f1" }]);
        });

        await hub.GetInFlightAsync("a");
        hub.Invalidate("a");
        await hub.GetInFlightAsync("a");

        Assert.Equal(2, calls);
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
