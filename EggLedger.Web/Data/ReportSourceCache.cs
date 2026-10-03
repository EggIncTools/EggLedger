using EggIdentity.Resilience;
using EggLedger.Domain.MissionQuery;
using EggLedger.Domain.Reports;
using EggLedger.Web.State;

namespace EggLedger.Web.Data;

public sealed record ReportSource(IReadOnlyList<MissionRowData> Missions, IReadOnlyList<ArtifactDropRowData> Drops, IReadOnlyList<FuelRowData> Fuel) {
    public static ReportSource Empty { get; } = new([], [], []);
}

public interface IReportSourceCache {
    Task<ReportSource> GetAsync(string accountId);
    void Invalidate(string accountId);
}

public sealed class ReportSourceCache : IReportSourceCache, IDisposable {
    public const int MaxAccounts = 3;

    public static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);

    private readonly Func<string, Task<ReportSource>> _loader;
    private readonly TimeSpan _ttl;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, TtlSnapshot<ReportSource>> _slots = [with(StringComparer.Ordinal)];
    private readonly List<string> _order = [];
    private Action? _detachHub;

    public ReportSourceCache(Func<string, Task<ReportSource>> loader, TimeProvider time)
        : this(loader, DefaultTtl, time) {
    }

    internal ReportSourceCache(
        Func<string, Task<ReportSource>> loader,
        TimeSpan ttl,
        TimeProvider time) {
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
        _ttl = ttl;
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    public Task<ReportSource> GetAsync(string accountId) {
        TtlSnapshot<ReportSource> slot;
        lock (_gate) {
            slot = Touch(accountId);
        }

        return slot.GetAsync();
    }

    public void Invalidate(string accountId) {
        lock (_gate) {
            _slots.Remove(accountId);
            _order.Remove(accountId);
        }
    }

    public void AttachHub(LedgerDataHub hub) {
        ArgumentNullException.ThrowIfNull(hub);
        if (_detachHub is not null) {
            return;
        }

        hub.AccountInvalidated += Invalidate;
        _detachHub = () => hub.AccountInvalidated -= Invalidate;
    }

    public void Dispose() {
        _detachHub?.Invoke();
        _detachHub = null;
    }

    private TtlSnapshot<ReportSource> Touch(string accountId) {
        if (_slots.TryGetValue(accountId, out var existing)) {
            _order.Remove(accountId);
            _order.Add(accountId);
            return existing;
        }

        var slot = new TtlSnapshot<ReportSource>(_ttl, async _ => await _loader(accountId).ConfigureAwait(false) ?? ReportSource.Empty, _time);
        _slots[accountId] = slot;
        _order.Add(accountId);
        while (_order.Count > MaxAccounts) {
            var evicted = _order[0];
            _order.RemoveAt(0);
            _slots.Remove(evicted);
        }

        return slot;
    }
}

public static class IndexedDbReportSource {
    public static Func<string, Task<ReportSource>> Loader(IIndexedDb db, IMissionStore missionStore) {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(missionStore);
        return accountId => LoadAsync(db, missionStore, accountId);
    }

    public static async Task<ReportSource> LoadAsync(IIndexedDb db, IMissionStore missionStore, string accountId) {
        ArgumentNullException.ThrowIfNull(missionStore);
        await missionStore.EnsureFilterColsBackfilledAsync(accountId).ConfigureAwait(false);
        return await LoadAsync(db, accountId).ConfigureAwait(false);
    }

    public static async Task<ReportSource> LoadAsync(IIndexedDb db, string accountId) {
        ArgumentNullException.ThrowIfNull(db);
        var missionRows = await db
            .GetAllByIndexAsync<MissionRow>(IndexedDbStores.Mission, IndexedDbStores.PlayerIdIndex, accountId)
            .ConfigureAwait(false);
        var dropRows = await db
            .GetAllByIndexAsync<ArtifactDropRow>(IndexedDbStores.ArtifactDrops, IndexedDbStores.PlayerIdIndex, accountId)
            .ConfigureAwait(false);
        var fuelRows = await db
            .GetAllByIndexAsync<FuelRow>(IndexedDbStores.MissionFuel, IndexedDbStores.PlayerIdIndex, accountId)
            .ConfigureAwait(false);

        return new ReportSource(
            [.. missionRows.Select(ToMissionData)],
            [.. dropRows.Select(ToDropData)],
            [.. fuelRows.Select(ToFuelData)]);
    }

    private static MissionRowData ToMissionData(MissionRow r) => new() {
        PlayerId = r.PlayerId,
        MissionId = r.MissionId,
        Ship = r.Ship,
        DurationType = r.DurationType,
        Level = r.Level,
        Target = r.Target,
        MissionType = r.MissionType,
        StartTimestamp = (long)r.StartTimestamp,
        ReturnTimestamp = (long)r.ReturnTimestamp,
        Capacity = r.Capacity,
        NominalCapacity = r.NominalCapacity,
        IsDubCap = r.IsDubCap,
        IsBuggedCap = r.IsBuggedCap,
    };

    private static ArtifactDropRowData ToDropData(ArtifactDropRow r) => new() {
        PlayerId = r.PlayerId,
        MissionId = r.MissionId,
        DropIndex = r.DropIndex,
        ArtifactId = r.ArtifactId,
        SpecType = r.SpecType,
        Level = r.Level,
        Rarity = r.Rarity,
        Quality = r.Quality,
    };

    private static FuelRowData ToFuelData(FuelRow r) => new() {
        PlayerId = r.PlayerId,
        MissionId = r.MissionId,
        EggId = r.EggId,
        Amount = r.Amount,
    };
}
