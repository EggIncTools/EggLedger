using Ei;

namespace EggLedger.Domain.MissionQuery;

public interface IMissionStore {
    Task<IReadOnlyList<string>?> GetCompleteMissionIdsAsync(string playerId);

    Task<IReadOnlyList<KnownAccount>> GetKnownAccountsAsync();

    Task<PlayerMissionStats?> GetPlayerMissionStatsAsync(string playerId);

    Task<bool> StreamPlayerCompleteMissionsAsync(string playerId, Action<CompleteMissionResponse> onMission);

    Task<CompleteMissionResponse?> GetCompleteMissionAsync(string playerId, string missionId);

    Task<int?> CountPendingFilterColsAsync(string eid);

    Task<IReadOnlyList<IMissionRow>?> GetPlayerMissionMetaAsync(string eid);

    Task<IReadOnlyList<CompleteMissionResponse>?> GetPlayerCompleteMissionsAsync(string eid);

    Task<IReadOnlyList<StoredDrop>?> GetStoredPlayerDropsAsync(string playerId);

    Task DeleteAllForPlayerAsync(string playerId);

    void QueueFilterColBackfill(string eid);

    Task EnsureFilterColsBackfilledAsync(string eid);

    void QueueArtifactDropsBackfill(string playerId);
}

public sealed record StoredDrop(string MissionId, int ArtifactId, int Level, int Rarity, int DropIndex = 0);

public interface IMissionRow {
}
