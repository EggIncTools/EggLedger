using EggLedger.Domain.MissionQuery;
using Ei;

namespace EggLedger.Domain.Tests.MissionQuery;

internal sealed class FakeMissionStore : IMissionStore {
    public IReadOnlyList<string>? CompleteMissionIds { get; set; }
    public List<KnownAccount> KnownAccounts { get; } = [];
    public Dictionary<string, PlayerMissionStats?> Stats { get; } = [];
    public Dictionary<string, List<CompleteMissionResponse>> Streamable { get; } = [];
    public bool StreamSucceeds { get; set; } = true;
    public Dictionary<(string, string), CompleteMissionResponse?> CompleteMissions { get; } = [];
    public Dictionary<string, int?> PendingFilterCols { get; } = [];
    public Dictionary<string, IReadOnlyList<IMissionRow>?> MissionMeta { get; } = [];
    public Dictionary<string, IReadOnlyList<CompleteMissionResponse>?> PlayerCompleteMissions { get; } = [];
    public Dictionary<string, IReadOnlyList<StoredDrop>?> StoredDrops { get; } = [];
    public List<string> BackfillsQueued { get; } = [];
    public List<string> DropsBackfillsQueued { get; } = [];

    public Task<IReadOnlyList<string>?> GetCompleteMissionIdsAsync(string playerId) =>
        Task.FromResult(CompleteMissionIds);

    public Task<IReadOnlyList<KnownAccount>> GetKnownAccountsAsync() =>
        Task.FromResult<IReadOnlyList<KnownAccount>>(KnownAccounts);

    public Task<PlayerMissionStats?> GetPlayerMissionStatsAsync(string playerId) =>
        Task.FromResult(Stats.TryGetValue(playerId, out var s) ? s : null);

    public Task<bool> StreamPlayerCompleteMissionsAsync(string playerId, Action<CompleteMissionResponse> onMission) {
        if (!StreamSucceeds) {
            return Task.FromResult(false);
        }
        if (Streamable.TryGetValue(playerId, out var list)) {
            foreach (var cm in list) {
                onMission(cm);
            }
        }
        return Task.FromResult(true);
    }

    public Task<CompleteMissionResponse?> GetCompleteMissionAsync(string playerId, string missionId) =>
        Task.FromResult(CompleteMissions.TryGetValue((playerId, missionId), out var cm) ? cm : null);

    public Task<int?> CountPendingFilterColsAsync(string eid) =>
        Task.FromResult<int?>(PendingFilterCols.TryGetValue(eid, out var p) ? p : 0);

    public Task<IReadOnlyList<IMissionRow>?> GetPlayerMissionMetaAsync(string eid) =>
        Task.FromResult(MissionMeta.TryGetValue(eid, out var m) ? m : null);

    public Task<IReadOnlyList<CompleteMissionResponse>?> GetPlayerCompleteMissionsAsync(string eid) =>
        Task.FromResult(PlayerCompleteMissions.TryGetValue(eid, out var m) ? m : null);

    public Task<IReadOnlyList<StoredDrop>?> GetStoredPlayerDropsAsync(string playerId) =>
        Task.FromResult(StoredDrops.TryGetValue(playerId, out var d) ? d : null);

    public List<string> DeletedPlayers { get; } = [];

    public Task DeleteAllForPlayerAsync(string playerId) {
        DeletedPlayers.Add(playerId);
        return Task.CompletedTask;
    }

    public void QueueFilterColBackfill(string eid) => BackfillsQueued.Add(eid);

    public void QueueArtifactDropsBackfill(string playerId) => DropsBackfillsQueued.Add(playerId);
}

internal sealed record FakeMissionRow(string Id) : IMissionRow;

internal sealed class FakeMissionCompiler : IMissionCompiler {
    public IMissionRow CompileMissionInformation(CompleteMissionResponse mission) =>
        new FakeMissionRow(mission.Info?.Identifier ?? "");
}

internal sealed class FakeQuality : IArtifactQuality {
    public Dictionary<(ArtifactSpec.Name, ArtifactSpec.Level, ArtifactSpec.Rarity), double> Map { get; } = [];

    public double BaseQualityFor(ArtifactSpec spec) =>
        Map.TryGetValue((spec.name, spec.level, spec.rarity), out var q) ? q : 0;
}
