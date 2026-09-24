using System.Globalization;
using EggLedger.Domain.Ei;
using EggLedger.Domain.Util;
using Ei;

namespace EggLedger.Domain.Export;

public sealed class Mission {
    public string Id { get; set; } = "";
    public MissionInfo.MissionType Type { get; set; }
    public string TypeName { get; set; } = "";
    public MissionInfo.Spaceship Ship { get; set; }
    public string ShipName { get; set; } = "";
    public MissionInfo.DurationType DurationType { get; set; }
    public string DurationTypeName { get; set; } = "";
    public uint Level { get; set; }
    public DateTimeOffset LaunchedAt { get; set; }
    public string LaunchedAtStr { get; set; } = "";
    public DateTimeOffset ReturnedAt { get; set; }
    public string ReturnedAtStr { get; set; } = "";
    public TimeSpan Duration { get; set; }
    public double DurationDays { get; set; }
    public uint Capacity { get; set; }
    public List<ArtifactSpec> Artifacts { get; set; } = [];
    public List<string> ArtifactNames { get; set; } = [];
    public ArtifactSpec.Name TargetArtifact { get; set; } = ArtifactSpec.Name.Unknown;

    private const string Rfc3339 = "yyyy-MM-ddTHH:mm:ssK";

    public static ArtifactSpec.Name CustomGetTargetArtifact(MissionInfo? mission) =>
        mission is not null
            && mission.ShouldSerializeTargetArtifact()
            && mission.StartTimeDerived >= 1686260700d
            ? mission.TargetArtifact
            : ArtifactSpec.Name.Unknown;

    public static string GetNamedTarget(ArtifactSpec.Name name) =>
        name != ArtifactSpec.Name.Unknown ? name.CasedName() : "";

    public static string MissionTypeName(int t) => t switch {
        0 => "Standard",
        1 => "Virtue",
        _ => "Unknown",
    };

    public static Mission FromResponse(CompleteMissionResponse r) {
        var info = r.Info ?? new MissionInfo();
        var ship = info.Ship;
        var durationType = info.duration_type;
        var launchedAt = Truncate(TimeFmt.UnixToTime(info.StartTimeDerived), TimeSpan.FromSeconds(1));
        double durationSeconds = info.DurationSeconds;
        var duration = TimeSpan.FromSeconds(durationSeconds);
        var returnedAt = launchedAt + duration;
        var target = CustomGetTargetArtifact(info);

        List<ArtifactSpec> artifacts = [];
        List<string> artifactNames = [];
        foreach (var a in r.Artifacts) {
            artifacts.Add(a.Spec);
            artifactNames.Add(a.Spec.Display());
        }

        return new Mission {
            Id = info.Identifier,
            Type = info.Type,
            TypeName = MissionTypeName((int)info.Type),
            Ship = ship,
            ShipName = ship.Name(),
            DurationType = durationType,
            DurationTypeName = durationType.Display(),
            Level = info.Level,
            LaunchedAt = launchedAt,
            LaunchedAtStr = launchedAt.ToString(Rfc3339, CultureInfo.InvariantCulture),
            ReturnedAt = returnedAt,
            ReturnedAtStr = returnedAt.ToString(Rfc3339, CultureInfo.InvariantCulture),
            Duration = duration,
            DurationDays = durationSeconds / 86400d,
            Capacity = info.Capacity,
            Artifacts = artifacts,
            ArtifactNames = artifactNames,
            TargetArtifact = target,
        };
    }

    private static DateTimeOffset Truncate(DateTimeOffset t, TimeSpan resolution) {
        long ticks = t.UtcTicks - (t.UtcTicks % resolution.Ticks);
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }
}
