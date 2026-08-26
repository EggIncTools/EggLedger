using EggLedger.Domain.MissionPacking;
using EggLedger.Domain.MissionQuery;
using EggLedger.Web.Data;
using EggLedger.Web.Missions;
using EggLedger.Web.Services;

namespace EggLedger.Web.State;

public sealed class LedgerDataHub : IDisposable {
    public const int MaxAccounts = 3;

    public const int MaxFilterMatchesPerAccount = 8;

    public static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);

    private readonly Func<string, Task<IReadOnlyList<DatabaseMission>?>> _missionsLoader;
    private readonly Func<string, Task<Dictionary<string, List<MissionDrop>>?>> _dropsLoader;
    private readonly Func<string, Task<IReadOnlyList<DatabaseMission>?>> _inFlightLoader;
    private readonly TimeSpan _ttl;
    private readonly Func<DateTime> _clock;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, AccountEntry> _entries = new(StringComparer.Ordinal);
    private readonly List<string> _order = [];
    private Action? _detachFetch;

    public LedgerDataHub(MissionQueryHandlers queries, IndexedDbMissionStore missions)
        : this(
            async accountId => (await queries.ViewMissionsOfEidAsync(accountId).ConfigureAwait(false))
                ?.Cast<DatabaseMission>().ToList(),
            queries.GetAllPlayerDropsAsync,
            DefaultTtl,
            inFlightLoader: async accountId =>
                await missions.GetInFlightMissionsAsync(accountId).ConfigureAwait(false)) {
    }

    internal LedgerDataHub(
        Func<string, Task<IReadOnlyList<DatabaseMission>?>> missionsLoader,
        Func<string, Task<Dictionary<string, List<MissionDrop>>?>> dropsLoader,
        TimeSpan ttl,
        Func<DateTime>? clock = null,
        Func<string, Task<IReadOnlyList<DatabaseMission>?>>? inFlightLoader = null) {
        _missionsLoader = missionsLoader;
        _dropsLoader = dropsLoader;
        _inFlightLoader = inFlightLoader ?? (_ => Task.FromResult<IReadOnlyList<DatabaseMission>?>([]));
        _ttl = ttl;
        _clock = clock ?? (() => DateTime.UtcNow);
    }

    public event Action<string>? AccountInvalidated;

    public Task<IReadOnlyList<DatabaseMission>?> GetMissionsAsync(string accountId) =>
        GetOrLoadAsync(accountId, static entry => entry.Missions, () => _missionsLoader(accountId));

    public Task<Dictionary<string, List<MissionDrop>>?> GetDropsAsync(string accountId) =>
        GetOrLoadAsync(accountId, static entry => entry.Drops, () => _dropsLoader(accountId));

    public Task<IReadOnlyList<DatabaseMission>?> GetInFlightAsync(string accountId) =>
        GetOrLoadAsync(accountId, static entry => entry.InFlightMissions, () => _inFlightLoader(accountId));

    public Task<LifetimeData?> GetLifetimeAggregateAsync(string accountId) =>
        GetOrLoadAsync(accountId, static entry => entry.Lifetime, async () => {
            var drops = await GetDropsAsync(accountId).ConfigureAwait(false);
            return drops is null ? null : LifetimeAggregator.Aggregate(drops);
        });

    public Task<IReadOnlySet<string>> GetFilterMatchesAsync(
        string accountId,
        string filterHash,
        Func<Task<IReadOnlySet<string>>> compute) {
        AccountEntry entry;
        TaskCompletionSource<IReadOnlySet<string>> completion;
        lock (_gate) {
            entry = Touch(accountId);
            if (entry.FilterMatches.TryGetValue(filterHash, out var hit)) {
                return hit;
            }

            completion = new TaskCompletionSource<IReadOnlySet<string>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            entry.FilterMatches[filterHash] = completion.Task;
            entry.FilterOrder.Add(filterHash);
            while (entry.FilterOrder.Count > MaxFilterMatchesPerAccount) {
                var oldest = entry.FilterOrder[0];
                entry.FilterOrder.RemoveAt(0);
                entry.FilterMatches.Remove(oldest);
            }
        }

        _ = FillFilterMatchesAsync(entry, filterHash, completion, compute);
        return completion.Task;
    }

    public void Invalidate(string accountId) {
        lock (_gate) {
            _entries.Remove(accountId);
            _order.Remove(accountId);
        }

        AccountInvalidated?.Invoke(accountId);
    }

    public void AttachFetch(FetchOrchestrator fetch) {
        if (_detachFetch is not null) {
            return;
        }

        fetch.FetchSucceeded += Invalidate;
        _detachFetch = () => fetch.FetchSucceeded -= Invalidate;
    }

    public void Dispose() {
        _detachFetch?.Invoke();
        _detachFetch = null;
    }

    private Task<T?> GetOrLoadAsync<T>(
        string accountId,
        Func<AccountEntry, CacheSlot<T>> pick,
        Func<Task<T?>> load) where T : class {
        CacheSlot<T> slot;
        TaskCompletionSource<T?> completion;
        lock (_gate) {
            slot = pick(Touch(accountId));
            if (slot.HasValue && _clock() - slot.LoadedAt < _ttl) {
                return Task.FromResult(slot.Value);
            }

            if (slot.InFlight is { } pending) {
                return pending;
            }

            completion = new TaskCompletionSource<T?>(TaskCreationOptions.RunContinuationsAsynchronously);
            slot.InFlight = completion.Task;
        }

        _ = FillAsync(slot, completion, load);
        return completion.Task;
    }

    private async Task FillAsync<T>(
        CacheSlot<T> slot,
        TaskCompletionSource<T?> completion,
        Func<Task<T?>> load) where T : class {
        try {
            var value = await load().ConfigureAwait(false);
            lock (_gate) {
                if (value is not null) {
                    slot.Value = value;
                    slot.LoadedAt = _clock();
                    slot.HasValue = true;
                }

                ClearInFlight(slot, completion);
            }

            completion.SetResult(value);
        } catch (Exception ex) {
            lock (_gate) {
                ClearInFlight(slot, completion);
            }

            completion.SetException(ex);
        }
    }

    private async Task FillFilterMatchesAsync(
        AccountEntry entry,
        string filterHash,
        TaskCompletionSource<IReadOnlySet<string>> completion,
        Func<Task<IReadOnlySet<string>>> compute) {
        try {
            var matches = await compute().ConfigureAwait(false);
            completion.SetResult(matches);
        } catch (Exception ex) {
            lock (_gate) {
                if (entry.FilterMatches.TryGetValue(filterHash, out var current)
                    && ReferenceEquals(current, completion.Task)) {
                    entry.FilterMatches.Remove(filterHash);
                    entry.FilterOrder.Remove(filterHash);
                }
            }

            completion.SetException(ex);
        }
    }

    private static void ClearInFlight<T>(CacheSlot<T> slot, TaskCompletionSource<T?> completion) where T : class {
        if (ReferenceEquals(slot.InFlight, completion.Task)) {
            slot.InFlight = null;
        }
    }

    private AccountEntry Touch(string accountId) {
        if (_entries.TryGetValue(accountId, out var existing)) {
            _order.Remove(accountId);
            _order.Add(accountId);
            return existing;
        }

        var entry = new AccountEntry();
        _entries[accountId] = entry;
        _order.Add(accountId);
        while (_order.Count > MaxAccounts) {
            var evicted = _order[0];
            _order.RemoveAt(0);
            _entries.Remove(evicted);
        }

        return entry;
    }

    private sealed class CacheSlot<T> where T : class {
        public T? Value { get; set; }
        public DateTime LoadedAt { get; set; }
        public bool HasValue { get; set; }
        public Task<T?>? InFlight { get; set; }
    }

    private sealed class AccountEntry {
        public CacheSlot<IReadOnlyList<DatabaseMission>> Missions { get; } = new();
        public CacheSlot<IReadOnlyList<DatabaseMission>> InFlightMissions { get; } = new();
        public CacheSlot<Dictionary<string, List<MissionDrop>>> Drops { get; } = new();
        public CacheSlot<LifetimeData> Lifetime { get; } = new();
        public Dictionary<string, Task<IReadOnlySet<string>>> FilterMatches { get; } = new(StringComparer.Ordinal);
        public List<string> FilterOrder { get; } = [];
    }
}
