using System.Globalization;
using EggLedger.Domain.LedgerData;
using Ei;

namespace EggLedger.Domain.Ei;

public static class MissionExtensions {
    private static LedgerDisplayData Config => LedgerData.LedgerData.Config;

    public static string Name(this MissionInfo.Spaceship s) =>
        Config.ShipNames.TryGetValue(EnumNames.ProtoName(s), out var name)
            ? name
            : EnumNames.ProtoName(s);

    public static string GetDurationString(this MissionInfo d) => GetDurationString(d.DurationSeconds);

    public static string GetDurationString(double seconds) {
        if (seconds == 0) {
            return "0m";
        }
        if (seconds < 60) {
            return $"{(int)seconds}s";
        }
        if (seconds < 3600) {
            return $"{(int)(seconds / 60)}m";
        }
        return seconds < 86400
            ? string.Format(
                CultureInfo.InvariantCulture,
                "{0}h{1}m",
                (int)(seconds / 3600),
                (int)(seconds / 60) % 60)
            : string.Format(
                CultureInfo.InvariantCulture,
                "{0}d{1}h{2}m",
                (int)(seconds / 86400),
                (int)(seconds / 3600) % 24,
                (int)(seconds / 60) % 60);
    }

    public static string Display(this MissionInfo.DurationType d) {
        return d switch {
            MissionInfo.DurationType.Tutorial => "Tutorial",
            MissionInfo.DurationType.Short => "Short",
            MissionInfo.DurationType.Long => "Standard",
            MissionInfo.DurationType.Epic => "Extended",
            _ => "Unknown",
        };
    }

    public static string Display(this MissionInfo.MissionType t) {
        return t switch {
            MissionInfo.MissionType.Standard => "Home",
            MissionInfo.MissionType.Virtue => "Virtue",
            _ => "Unknown",
        };
    }

    public static List<MissionInfo> GetCompletedMissions(this EggIncFirstContactResponse fc) {
        var afxdb = fc.Backup?.ArtifactsDb;
        List<MissionInfo> allMissions = [];
        if (afxdb is not null) {
            allMissions.AddRange(afxdb.MissionArchives);
            allMissions.AddRange(afxdb.MissionInfos);
        }

        List<MissionInfo> completed = [];

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var mission in allMissions) {
            var status = mission.status;
            if (status is MissionInfo.Status.Complete or MissionInfo.Status.Archived) {
                var id = mission.Identifier;
                if (seen.Add(id)) {
                    completed.Add(mission);
                }
            }
        }

        return StableSortByStartTime(completed);
    }

    public static List<MissionInfo> GetInProgressMissions(this EggIncFirstContactResponse fc) {
        var afxdb = fc.Backup?.ArtifactsDb;
        List<MissionInfo> inProgress = afxdb is null
            ? []
            : [.. afxdb.MissionInfos.Where(m => m.status is MissionInfo.Status.Exploring
                or MissionInfo.Status.Fueling
                or MissionInfo.Status.PrepareToLaunch)];
        return StableSortByStartTime(inProgress);
    }

    private static List<MissionInfo> StableSortByStartTime(List<MissionInfo> missions) =>
        [.. missions
            .Select((m, i) => (m, i))
            .OrderBy(x => x.m.StartTimeDerived)
            .ThenBy(x => x.i)
            .Select(x => x.m)];
}
