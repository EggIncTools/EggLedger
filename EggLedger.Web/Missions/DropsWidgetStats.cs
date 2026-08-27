using EggLedger.Domain.MissionPacking;

namespace EggLedger.Web.Missions;

public sealed record ShipCount(string ShipEnumString, string ShipName, int Count);

public sealed record DurationCount(string DurationName, int Count);

public sealed record RarityCount(int Rarity, int Count);

public sealed class DropsWidgetStats {
    public int MissionCount { get; init; }
    public long OldestLaunchDT { get; init; }
    public double TotalAirtimeHours { get; init; }
    public int HomeCount { get; init; }
    public int VirtueCount { get; init; }
    public int DubCapCount { get; init; }
    public int BuggedCapCount { get; init; }
    public IReadOnlyList<ShipCount> Ships { get; init; } = [];
    public IReadOnlyList<DurationCount> Durations { get; init; } = [];
    public IReadOnlyList<RarityCount> ArtifactRarities { get; init; } = [];

    private const int MaxShipRows = 5;

    public static DropsWidgetStats Compute(IReadOnlyList<DatabaseMission> missions, IReadOnlyList<DropLike>? artifacts) {
        if (missions.Count == 0) {
            return new DropsWidgetStats();
        }

        var shipCounts = new Dictionary<string, (string Name, int Count)>(StringComparer.Ordinal);
        var durationCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var oldestLaunch = long.MaxValue;
        double airtimeSeconds = 0;
        var home = 0;
        var virtue = 0;
        var dubCap = 0;
        var buggedCap = 0;

        foreach (var m in missions) {
            var shipKey = m.ShipEnumString.Length > 0 ? m.ShipEnumString : m.ShipString;
            if (shipCounts.TryGetValue(shipKey, out var existing)) {
                shipCounts[shipKey] = (existing.Name, existing.Count + 1);
            } else {
                shipCounts[shipKey] = (m.ShipString, 1);
            }

            durationCounts[m.DurationString] = durationCounts.GetValueOrDefault(m.DurationString) + 1;

            if (m.LaunchDT < oldestLaunch) {
                oldestLaunch = m.LaunchDT;
            }

            airtimeSeconds += Math.Max(0, m.ReturnDT - m.LaunchDT);

            if (m.MissionType == 0) {
                home++;
            } else {
                virtue++;
            }

            if (m.IsBuggedCap) {
                buggedCap++;
            } else if (m.IsDubCap) {
                dubCap++;
            }
        }

        var rarityCounts = new Dictionary<int, int>();
        if (artifacts is not null) {
            foreach (var item in artifacts) {
                rarityCounts[item.Rarity] = rarityCounts.GetValueOrDefault(item.Rarity) + item.Count;
            }
        }

        return new DropsWidgetStats {
            MissionCount = missions.Count,
            OldestLaunchDT = oldestLaunch,
            TotalAirtimeHours = airtimeSeconds / 3600.0,
            HomeCount = home,
            VirtueCount = virtue,
            DubCapCount = dubCap,
            BuggedCapCount = buggedCap,
            Ships = TopShips(shipCounts),
            Durations = [.. durationCounts
                .Select(kv => new DurationCount(kv.Key, kv.Value))
                .OrderByDescending(d => d.Count)],
            ArtifactRarities = [.. rarityCounts
                .Select(kv => new RarityCount(kv.Key, kv.Value))
                .OrderByDescending(r => r.Rarity)],
        };
    }

    private static List<ShipCount> TopShips(Dictionary<string, (string Name, int Count)> shipCounts) {
        var ordered = shipCounts
            .Select(kv => new ShipCount(kv.Key, kv.Value.Name, kv.Value.Count))
            .OrderByDescending(s => s.Count)
            .ToList();

        if (ordered.Count <= MaxShipRows) {
            return ordered;
        }

        var top = ordered.Take(MaxShipRows - 1).ToList();
        var otherCount = ordered.Skip(MaxShipRows - 1).Sum(s => s.Count);
        top.Add(new ShipCount("", "Other", otherCount));
        return top;
    }
}
