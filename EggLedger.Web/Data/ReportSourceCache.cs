using EggLedger.Domain.MissionQuery;
using EggLedger.Domain.Reports;
using EggLedger.Web.State;

namespace EggLedger.Web.Data;

public interface IReportSourceCache {
    Task<LedgerReportData> GetAsync(string accountId);
    void Invalidate(string accountId);
}

public sealed class ReportSourceCache(LedgerDataStore store, IDataOwner owner, Func<string, Task<LedgerReportData>> loader) : IReportSourceCache, IDisposable {
    private Action? _detachHub;

    public async Task<LedgerReportData> GetAsync(string accountId) {
        var key = await owner.KeyAsync().ConfigureAwait(false);
        return await store.GetAsync(key, accountId, () => loader(accountId)).ConfigureAwait(false);
    }

    public void Invalidate(string accountId) {
        if (owner.CachedKey is { } key) store.Invalidate(key, accountId);
        else store.InvalidateAccount(accountId);
    }

    public void AttachHub(LedgerDataHub hub) {
        ArgumentNullException.ThrowIfNull(hub);
        if (_detachHub is not null) return;

        hub.AccountInvalidated += Invalidate;
        _detachHub = () => hub.AccountInvalidated -= Invalidate;
    }

    public void Dispose() {
        _detachHub?.Invoke();
        _detachHub = null;
    }
}

public static class IndexedDbReportSource {
    public static Func<string, Task<LedgerReportData>> Loader(IIndexedDb db, IMissionStore missionStore) {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(missionStore);
        return accountId => LoadAsync(db, missionStore, accountId);
    }

    public static async Task<LedgerReportData> LoadAsync(IIndexedDb db, IMissionStore missionStore, string accountId) {
        ArgumentNullException.ThrowIfNull(missionStore);
        await missionStore.EnsureFilterColsBackfilledAsync(accountId).ConfigureAwait(false);
        return await LoadAsync(db, accountId).ConfigureAwait(false);
    }

    public static async Task<LedgerReportData> LoadAsync(IIndexedDb db, string accountId) {
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

        return new LedgerReportData(
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
