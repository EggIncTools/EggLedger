using EggLedger.Domain.MissionPacking;
using EggLedger.Domain.MissionQuery;
using EggLedger.Domain.Reports;
using EggLedger.Web.Data;
using EggLedger.Web.Missions;
using EggLedger.Web.Services;

namespace EggLedger.Web.State;

public sealed class LedgerDataHub : IDisposable {
    public const int MaxFilterMatchesPerAccount = 8;

    private const string MissionsKey = "missions";
    private const string DropsKey = "drops";
    private const string LifetimeKey = "lifetime";

    private readonly LedgerDataStore _store;
    private readonly IDataOwner _owner;
    private readonly Func<string, Task<LedgerReportData>> _loader;
    private readonly MissionPacker _packer;
    private readonly MissionQueryHandlers _queries;
    private readonly Func<string, Task<IReadOnlyList<DatabaseMission>?>> _inFlightLoader;
    private readonly Action<string>? _onAccountTouched;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, AccountEntry> _entries = [with(StringComparer.Ordinal)];
    private Action? _detachFetch;

    public LedgerDataHub(
        LedgerDataStore store,
        IDataOwner owner,
        Func<string, Task<LedgerReportData>> loader,
        MissionPacker packer,
        MissionQueryHandlers queries,
        IMissionStore missions,
        Func<string, Task<IReadOnlyList<DatabaseMission>?>>? inFlightLoader = null) {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
        _packer = packer ?? throw new ArgumentNullException(nameof(packer));
        _queries = queries ?? throw new ArgumentNullException(nameof(queries));
        ArgumentNullException.ThrowIfNull(missions);
        _inFlightLoader = inFlightLoader ?? (_ => Task.FromResult<IReadOnlyList<DatabaseMission>?>([]));
        _onAccountTouched = missions.QueueArtifactDropsBackfill;
    }

    internal LedgerDataHub(
        LedgerDataStore store,
        IDataOwner owner,
        Func<string, Task<LedgerReportData>> loader,
        MissionPacker packer,
        MissionQueryHandlers queries,
        Func<string, Task<IReadOnlyList<DatabaseMission>?>>? inFlightLoader = null) {
        _store = store;
        _owner = owner;
        _loader = loader;
        _packer = packer;
        _queries = queries;
        _inFlightLoader = inFlightLoader ?? (_ => Task.FromResult<IReadOnlyList<DatabaseMission>?>([]));
    }

    public event Action<string>? AccountInvalidated;

    public async Task<IReadOnlyList<DatabaseMission>?> GetMissionsAsync(string accountId) {
        _onAccountTouched?.Invoke(accountId);
        return await DerivedAsync(accountId, MissionsKey, data => Task.Run(() => Missions(data))).ConfigureAwait(false);
    }

    public Task<Dictionary<string, List<MissionDrop>>?> GetDropsAsync(string accountId) =>
        DerivedAsync(accountId, DropsKey, data => Task.Run(() => Drops(data)));

    public Task<LifetimeData?> GetLifetimeAggregateAsync(string accountId) =>
        DerivedAsync(accountId, LifetimeKey, data => Task.Run(() => LifetimeAggregator.Aggregate(Drops(data))));

    public Task<IReadOnlyList<DatabaseMission>?> GetInFlightAsync(string accountId) {
        AccountEntry entry;
        lock (_gate) {
            entry = Touch(accountId);
            if (entry.InFlight is { } pending) return pending;
            var load = LoadInFlightAsync(entry, accountId);
            if (!load.IsFaulted) entry.InFlight = load;
            return load;
        }
    }

    public Task<IReadOnlySet<string>> GetFilterMatchesAsync(
        string accountId,
        string filterHash,
        Func<Task<IReadOnlySet<string>>> compute) {
        AccountEntry entry;
        TaskCompletionSource<IReadOnlySet<string>> completion;
        lock (_gate) {
            entry = Touch(accountId);
            if (entry.FilterMatches.TryGetValue(filterHash, out var hit)) return hit;

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

        _ = Task.Run(() => FillFilterMatchesAsync(entry, filterHash, completion, compute));
        return completion.Task;
    }

    public void Invalidate(string accountId) {
        lock (_gate) {
            _entries.Remove(accountId);
        }

        if (_owner.CachedKey is { } key) _store.Invalidate(key, accountId);
        else _store.InvalidateAccount(accountId);
        AccountInvalidated?.Invoke(accountId);
    }

    public void AttachFetch(FetchOrchestrator fetch) {
        if (_detachFetch is not null) return;

        fetch.FetchSucceeded += Invalidate;
        _detachFetch = () => fetch.FetchSucceeded -= Invalidate;
    }

    public void Dispose() {
        _detachFetch?.Invoke();
        _detachFetch = null;
        lock (_gate) {
            _entries.Clear();
        }
    }

    private async Task<T?> DerivedAsync<T>(string accountId, string name, Func<LedgerReportData, Task<T>> derive) where T : class {
        var key = await _owner.KeyAsync().ConfigureAwait(false);
        return await _store.GetDerivedAsync(key, accountId, name, () => Task.Run(() => _loader(accountId)), derive).ConfigureAwait(false);
    }

    private IReadOnlyList<DatabaseMission> Missions(LedgerReportData data) => [.. data.Missions
        .Where(r => r.Ship != -1)
        .OrderBy(r => r.StartTimestamp)
        .Select(r => _packer.MissionMetaToDBMission(new MissionMeta {
            MissionId = r.MissionId,
            StartTimestamp = r.StartTimestamp,
            ReturnTimestamp = r.ReturnTimestamp,
            Ship = (int)r.Ship,
            DurationType = (int)r.DurationType,
            Level = (int)r.Level,
            Capacity = (int)r.Capacity,
            NominalCapacity = (int)r.NominalCapacity,
            IsDubCap = r.IsDubCap,
            IsBuggedCap = r.IsBuggedCap,
            Target = (int)r.Target,
            MissionType = (int)r.MissionType,
        }))];

    private Dictionary<string, List<MissionDrop>> Drops(LedgerReportData data) => _queries.BuildDrops(
        data.Missions.OrderBy(r => r.StartTimestamp).Select(r => r.MissionId),
        data.Drops.Select(d => new StoredDrop(d.MissionId, (int)d.ArtifactId, (int)d.Level, (int)d.Rarity, (int)d.DropIndex)));

    private async Task<IReadOnlyList<DatabaseMission>?> LoadInFlightAsync(AccountEntry entry, string accountId) {
        try {
            return await _inFlightLoader(accountId).ConfigureAwait(false);
        } catch {
            lock (_gate) {
                entry.InFlight = null;
            }
            throw;
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

    private AccountEntry Touch(string accountId) {
        if (_entries.TryGetValue(accountId, out var existing)) return existing;
        var entry = new AccountEntry();
        _entries[accountId] = entry;
        return entry;
    }

    private sealed class AccountEntry {
        public Task<IReadOnlyList<DatabaseMission>?>? InFlight { get; set; }
        public Dictionary<string, Task<IReadOnlySet<string>>> FilterMatches { get; } = [with(StringComparer.Ordinal)];
        public List<string> FilterOrder { get; } = [];
    }
}
