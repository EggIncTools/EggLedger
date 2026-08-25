using EggLedger.Domain.Ei;
using EggLedger.Domain.MissionQuery;
using Ei;

namespace EggLedger.Domain.MissionPacking;

public sealed class MissionPacker : IMissionCompiler {
    private const long BuggedCapLower = 1712721600;
    private const long BuggedCapUpper = 1713286800;

    private readonly IMissionConfigSource _configSource;
    private readonly Lazy<Dictionary<MissionInfo.Spaceship, Dictionary<MissionInfo.DurationType, float[]>>> _caps;

    public MissionPacker(IMissionConfigSource configSource) {
        _configSource = configSource;
        _caps = new(BuildNominalShipCapacities);
    }

    private Dictionary<MissionInfo.Spaceship, Dictionary<MissionInfo.DurationType, float[]>> BuildNominalShipCapacities() {
        var table = new Dictionary<MissionInfo.Spaceship, Dictionary<MissionInfo.DurationType, float[]>>();
        foreach (var mission in _configSource.Config.mission_parameters) {
            var byDuration = new Dictionary<MissionInfo.DurationType, float[]>();
            table[mission.Ship] = byDuration;

            int levelCount = mission.LevelMissionRequirements?.Length ?? 0;
            foreach (var duration in mission.Durations) {
                float capacity = duration.Capacity;
                if (levelCount == 0) {
                    byDuration[duration.DurationType] = [capacity];
                } else {
                    var levels = new float[levelCount + 1];
                    float bump = duration.LevelCapacityBump;
                    for (int level = 0; level <= levelCount; level++) {
                        levels[level] = capacity + (bump * level);
                    }
                    byDuration[duration.DurationType] = levels;
                }
            }
        }
        return table;
    }

    public bool TryGetShipCapacities(MissionInfo.Spaceship ship, MissionInfo.DurationType dur, out float[] caps) {
        if (_caps.Value.TryGetValue(ship, out var byDuration)
            && byDuration.TryGetValue(dur, out var found)) {
            caps = found;
            return true;
        }
        caps = [];
        return false;
    }

    private static string ProperTargetName(ArtifactSpec.Name? name) =>
        name is null ? "" : EnumNames.ProtoName(name.Value);

    public static string DurationStringFromSecs(double seconds) =>
        MissionExtensions.GetDurationString(seconds);

    public bool TryComputeMissionFilterCols(double startTimestamp, CompleteMissionResponse resp, out MissionFilterCols cols) {
        var info = resp.Info;
        if (info is null) {
            cols = default;
            return false;
        }

        var ship = info.Ship;
        var durationType = info.duration_type;
        int level = (int)info.Level;
        int capacity = (int)info.Capacity;
        double durationSeconds = info.DurationSeconds;
        double returnTimestamp = startTimestamp + durationSeconds;

        int target = -1;
        if (info.ShouldSerializeTargetArtifact()) {
            target = (int)info.TargetArtifact;
        }

        float nominalCap = 0;
        if (TryGetShipCapacities(ship, durationType, out var caps) && level < caps.Length) {
            nominalCap = caps[level];
        }
        bool isDubCap = nominalCap > 0 && (float)capacity >= nominalCap * 1.7f;
        bool isBuggedCap = startTimestamp is > BuggedCapLower and < BuggedCapUpper;

        cols = new MissionFilterCols {
            Ship = (int)ship,
            DurationType = (int)durationType,
            Level = level,
            Capacity = capacity,
            NominalCapacity = (int)nominalCap,
            IsDubCap = isDubCap,
            IsBuggedCap = isBuggedCap,
            Target = target,
            ReturnTimestamp = returnTimestamp,
        };
        return true;
    }

    public DatabaseMission MissionMetaToDBMission(MissionMeta meta) {
        var ship = (MissionInfo.Spaceship)meta.Ship;
        var durationType = (MissionInfo.DurationType)meta.DurationType;

        float nominalCap = 0;
        if (TryGetShipCapacities(ship, durationType, out var caps) && meta.Level < caps.Length) {
            nominalCap = caps[meta.Level];
        }

        string target = "";
        int targetInt = -1;
        if (meta.Target >= 0) {
            var targetName = (ArtifactSpec.Name)meta.Target;
            target = ProperTargetName(targetName);
            targetInt = meta.Target;
        }

        var missionType = (MissionInfo.MissionType)meta.MissionType;
        double durationSecs = meta.ReturnTimestamp - meta.StartTimestamp;

        return new DatabaseMission {
            LaunchDT = (long)meta.StartTimestamp,
            ReturnDT = (long)meta.ReturnTimestamp,
            MissiondId = meta.MissionId,
            Ship = ship,
            ShipString = ship.Name(),
            DurationType = durationType,
            DurationString = DurationStringFromSecs(durationSecs),
            Level = meta.Level,
            Capacity = meta.Capacity,
            NominalCapcity = (int)nominalCap,
            IsDubCap = meta.IsDubCap,
            IsBuggedCap = meta.IsBuggedCap,
            Target = target,
            TargetInt = targetInt,
            MissionType = meta.MissionType,
            MissionTypeString = missionType.Display(),
            ShipEnumString = EnumNames.ProtoName(ship),
        };
    }

    public DatabaseMission CompileInFlightMission(MissionInfo info) {
        long launch = (long)info.StartTimeDerived;
        long returnDt = launch + (long)Math.Floor(info.DurationSeconds);

        float nominalCap = 0;
        if (TryGetShipCapacities(info.Ship, info.duration_type, out var caps) && (int)info.Level < caps.Length) {
            nominalCap = caps[(int)info.Level];
        }

        var mission = new DatabaseMission {
            LaunchDT = launch,
            ReturnDT = returnDt,
            DurationString = info.GetDurationString(),
            MissiondId = info.Identifier,
            Ship = info.ShouldSerializeShip() ? info.Ship : null,
            ShipString = info.Ship.Name(),
            DurationType = info.ShouldSerializeduration_type() ? info.duration_type : null,
            Level = (int)info.Level,
            Capacity = (int)info.Capacity,
            NominalCapcity = (int)nominalCap,
            Target = ProperTargetName(info.ShouldSerializeTargetArtifact() ? info.TargetArtifact : null),
            MissionType = (int)info.Type,
            MissionTypeString = info.Type.Display(),
            ShipEnumString = EnumNames.ProtoName(info.Ship),
        };
        mission.TargetInt = mission.Target.Length == 0 ? -1 : (int)info.TargetArtifact;
        return mission;
    }

    private static int ResolveMissionType(int dbMissionType, CompleteMissionResponse mission) {
        int missionType = dbMissionType;
        if (missionType == -1) {
            var info = mission.Info;
            if (info is not null) {
                missionType = (int)info.Type;
            }
        }
        return missionType;
    }

    IMissionRow IMissionCompiler.CompileMissionInformation(CompleteMissionResponse mission) =>
        CompileMissionInformation(mission);

    public DatabaseMission CompileMissionInformation(CompleteMissionResponse completeMissionResponse) {
        var info = completeMissionResponse.Info;
        if (info is null) {
            return new DatabaseMission();
        }

        long launch = (long)info.StartTimeDerived;

        long returnDt = launch + (long)Math.Floor(info.DurationSeconds);

        float nominalCap = 0;
        if (TryGetShipCapacities(info.Ship, info.duration_type, out var caps) && (int)info.Level < caps.Length) {
            nominalCap = caps[(int)info.Level];
        }
        int mType = ResolveMissionType(-1, completeMissionResponse);

        var mission = new DatabaseMission {
            LaunchDT = launch,
            ReturnDT = returnDt,
            DurationString = info.GetDurationString(),
            MissiondId = info.Identifier,
            Ship = info.ShouldSerializeShip() ? info.Ship : null,
            ShipString = info.Ship.Name(),
            DurationType = info.ShouldSerializeduration_type() ? info.duration_type : null,
            Level = (int)info.Level,
            Capacity = (int)info.Capacity,
            NominalCapcity = (int)nominalCap,
            IsDubCap = IsDubCap(completeMissionResponse),
            IsBuggedCap = IsBuggedCap(completeMissionResponse),
            Target = ProperTargetName(info.ShouldSerializeTargetArtifact() ? info.TargetArtifact : null),
            MissionType = mType,
            MissionTypeString = ((MissionInfo.MissionType)mType).Display(),
            ShipEnumString = EnumNames.ProtoName(info.Ship),
        };
        if (mission.Target.Length == 0) {
            mission.TargetInt = -1;
        } else {
            mission.TargetInt = (int)info.TargetArtifact;
        }

        return mission;
    }

    public bool IsDubCap(CompleteMissionResponse mission) {
        if (mission.Info is null) {
            return false;
        }
        var info = mission.Info;
        int level = (int)info.Level;
        if (!TryGetShipCapacities(info.Ship, info.duration_type, out var caps) || level >= caps.Length) {
            return false;
        }
        float nominalCapacity = caps[level];
        return (float)info.Capacity >= nominalCapacity * 1.7f;
    }

    public bool IsBuggedCap(CompleteMissionResponse mission) {
        if (mission.Info is null) {
            return false;
        }
        return mission.Info.StartTimeDerived is > BuggedCapLower and < BuggedCapUpper;
    }
}
