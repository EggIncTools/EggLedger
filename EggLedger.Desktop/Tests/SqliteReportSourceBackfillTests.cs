using EggLedger.Desktop.Storage;
using EggLedger.Domain.MissionQuery;
using Ei;

namespace EggLedger.Desktop.Tests;

public sealed class SqliteReportSourceBackfillTests {
    private const string Eid = "EI1";

    private sealed class RecordingMissionStore : IMissionStore {
        public List<string> Ensured { get; } = [];

        public Task<IReadOnlyList<string>?> GetCompleteMissionIdsAsync(string playerId) =>
            Task.FromResult<IReadOnlyList<string>?>(null);
        public Task<IReadOnlyList<KnownAccount>> GetKnownAccountsAsync() =>
            Task.FromResult<IReadOnlyList<KnownAccount>>([]);
        public Task<PlayerMissionStats?> GetPlayerMissionStatsAsync(string playerId) =>
            Task.FromResult<PlayerMissionStats?>(null);
        public Task<bool> StreamPlayerCompleteMissionsAsync(string playerId, Action<CompleteMissionResponse> onMission) =>
            Task.FromResult(false);
        public Task<CompleteMissionResponse?> GetCompleteMissionAsync(string playerId, string missionId) =>
            Task.FromResult<CompleteMissionResponse?>(null);
        public Task<int?> CountPendingFilterColsAsync(string eid) => Task.FromResult<int?>(0);
        public Task<IReadOnlyList<IMissionRow>?> GetPlayerMissionMetaAsync(string eid) =>
            Task.FromResult<IReadOnlyList<IMissionRow>?>(null);
        public Task<IReadOnlyList<CompleteMissionResponse>?> GetPlayerCompleteMissionsAsync(string eid) =>
            Task.FromResult<IReadOnlyList<CompleteMissionResponse>?>(null);
        public Task<IReadOnlyList<StoredDrop>?> GetStoredPlayerDropsAsync(string playerId) =>
            Task.FromResult<IReadOnlyList<StoredDrop>?>(null);
        public Task DeleteAllForPlayerAsync(string playerId) => Task.CompletedTask;
        public void QueueFilterColBackfill(string eid) {
        }
        public Task EnsureFilterColsBackfilledAsync(string eid) {
            Ensured.Add(eid);
            return Task.CompletedTask;
        }
        public void QueueArtifactDropsBackfill(string playerId) {
        }
    }

    [Fact]
    public async Task LoadAsync_BackfillsFilterColsBeforeQueryingSqlite() {
        var tag = Guid.NewGuid().ToString("N");
        using var missionDb = SqliteDatabase.Open($"Data Source=m_{tag};Mode=Memory;Cache=Shared", isConnectionString: true);
        SqliteMigrationRunner.MigrateMissionDb(missionDb.Connection);
        var db = new SqliteMissionDb(missionDb);
        var missionStore = new RecordingMissionStore();

        var source = await SqliteReportSource.LoadAsync(db, missionStore, Eid);

        Assert.Equal([Eid], missionStore.Ensured);
        Assert.Empty(source.Missions);
    }
}
