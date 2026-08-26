using EggLedger.Domain.Ei;
using Ei;

namespace EggLedger.Domain.MissionQuery;

public sealed class MissionQueryHandlers(IMissionStore store, IArtifactQuality quality, IMissionCompiler compiler) {
    public Task<IReadOnlyList<string>?> GetMissionIdsAsync(string playerId) =>
        store.GetCompleteMissionIdsAsync(playerId);

    public async Task<List<DatabaseAccount>> GetExistingDataAsync() {
        var result = new List<DatabaseAccount>();
        foreach (var acct in await store.GetKnownAccountsAsync()) {
            var stats = await store.GetPlayerMissionStatsAsync(acct.Id);
            if (stats is null) {
                continue;
            }
            if (stats.Value.Count > 0) {
                result.Add(new DatabaseAccount {
                    Id = acct.Id,
                    Nickname = acct.Nickname,
                    MissionCount = stats.Value.Count,
                    EBString = acct.EBString,
                    AccountColor = acct.AccountColor,
                    LastMissionReturnDT = stats.Value.MaxReturnTimestamp,
                });
            }
        }
        return result;
    }

    public async Task<IReadOnlyList<IMissionRow>?> ViewMissionsOfEidAsync(string eid) {


        store.QueueArtifactDropsBackfill(eid);

        int? pending = await store.CountPendingFilterColsAsync(eid);


        if (pending is 0) {
            return await store.GetPlayerMissionMetaAsync(eid);
        }


        var complete = await store.GetPlayerCompleteMissionsAsync(eid);
        if (complete is null) {
            return null;
        }
        var missions = new List<IMissionRow>(complete.Count);
        foreach (var cm in complete) {
            missions.Add(compiler.CompileMissionInformation(cm));
        }


        if (pending is > 0) {
            store.QueueFilterColBackfill(eid);
        }

        return missions;
    }

    public static List<PossibleMission> GetDurationConfigs(
        IEnumerable<ArtifactsConfigurationResponse.MissionParameters> missionParameters) {
        var result = new List<PossibleMission>();
        foreach (var mission in missionParameters) {
            int maxLevels = mission.LevelMissionRequirements?.Length ?? 0;
            var durations = new List<DurationConfig>();
            foreach (var d in mission.Durations) {
                durations.Add(new DurationConfig {
                    DurationType = d.DurationType,
                    MinQuality = d.MinQuality,
                    MaxQuality = d.MaxQuality,
                    LevelQualityBump = d.LevelQualityBump,
                    MaxLevels = maxLevels,
                });
            }
            result.Add(new PossibleMission { Ship = mission.Ship, Durations = durations });
        }
        return result;
    }

    public async Task<Dictionary<string, List<MissionDrop>>?> GetAllPlayerDropsAsync(string playerId) {



        var stored = await store.GetStoredPlayerDropsAsync(playerId);
        if (stored is null) {
            return null;
        }
        var missionIds = await store.GetCompleteMissionIdsAsync(playerId);
        if (missionIds is null) {
            return null;
        }

        var result = new Dictionary<string, List<MissionDrop>>();
        foreach (var id in missionIds) {
            result[id] = [];
        }
        foreach (var d in stored) {
            if (d.DropIndex < 0) {
                continue;
            }
            var spec = new ArtifactSpec {
                name = (ArtifactSpec.Name)d.ArtifactId,
                level = (ArtifactSpec.Level)d.Level,
                rarity = (ArtifactSpec.Rarity)d.Rarity,
            };
            if (!result.TryGetValue(d.MissionId, out var drops)) {
                drops = [];
                result[d.MissionId] = drops;
            }
            drops.Add(ShapeDrop(spec));
        }
        return result;
    }

    public async Task<List<MissionDrop>?> GetShipDropsAsync(string playerId, string missionId) {
        var cm = await store.GetCompleteMissionAsync(playerId, missionId);
        if (cm is null) {
            return null;
        }
        var drops = new List<MissionDrop>();
        foreach (var artifact in cm.Artifacts) {
            var spec = artifact.Spec;
            if (spec is not null) {
                drops.Add(ShapeDrop(spec));
            }
        }
        return drops;
    }

    private MissionDrop ShapeDrop(ArtifactSpec spec) {
        string protoName = EnumNames.ProtoName(spec.name);
        var drop = new MissionDrop {
            Id = (int)spec.name,
            Name = protoName,
            GameName = spec.CasedName(),
            Level = (int)spec.level,
            Rarity = (int)spec.rarity,
            Quality = quality.BaseQualityFor(spec),
            IVOrder = spec.name.InventoryVisualizerOrder(),
        };

        if (protoName.Contains("_FRAGMENT", StringComparison.Ordinal)) {
            drop.SpecType = "StoneFragment";
        } else if (protoName.Contains("_STONE", StringComparison.Ordinal)) {
            drop.SpecType = "Stone";
            drop.EffectString = spec.CombinedEffectString();
        } else if (protoName.Contains("GOLD_METEORITE", StringComparison.Ordinal)
                   || protoName.Contains("SOLAR_TITANIUM", StringComparison.Ordinal)
                   || protoName.Contains("TAU_CETI_GEODE", StringComparison.Ordinal)) {
            drop.SpecType = "Ingredient";
        } else {
            drop.SpecType = "Artifact";
            drop.EffectString = spec.CombinedEffectString();
        }

        return drop;
    }
}
