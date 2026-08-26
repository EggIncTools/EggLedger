using EggLedger.Domain.MissionPacking;
using Ei;

namespace EggLedger.Web.Missions.Timeline;

public sealed record FlightRatioSlice(bool IsVirtue, double Ratio);

public static class TimelineFlightRatio {
    public const int AssumedConcurrency = 3;

    public static double? Compute(
        IReadOnlyList<DatabaseMission> missions,
        DateTimeOffset visibleStart,
        DateTimeOffset visibleEnd,
        DateTimeOffset now) {
        return ComputeByFarm(missions, visibleStart, visibleEnd, now) is { } slices
            ? slices.Sum(s => s.Ratio)
            : null;
    }

    public static IReadOnlyList<FlightRatioSlice>? ComputeByFarm(
        IReadOnlyList<DatabaseMission> missions,
        DateTimeOffset visibleStart,
        DateTimeOffset visibleEnd,
        DateTimeOffset now) {
        long winStart = visibleStart.ToUnixTimeSeconds();
        long winEnd = Math.Min(visibleEnd.ToUnixTimeSeconds(), now.ToUnixTimeSeconds());
        if (winEnd <= winStart) {
            return null;
        }

        long homeFlown = 0;
        long virtueFlown = 0;
        foreach (var m in missions) {
            long start = Math.Max(m.LaunchDT, winStart);
            long end = Math.Min(m.ReturnDT, winEnd);
            if (end <= start) {
                continue;
            }
            if (m.MissionType == (int)MissionInfo.MissionType.Virtue) {
                virtueFlown += end - start;
            } else {
                homeFlown += end - start;
            }
        }

        double denominator = AssumedConcurrency * (double)(winEnd - winStart);
        var slices = new List<FlightRatioSlice>(2);
        if (homeFlown > 0) {
            slices.Add(new FlightRatioSlice(false, homeFlown / denominator));
        }
        if (virtueFlown > 0) {
            slices.Add(new FlightRatioSlice(true, virtueFlown / denominator));
        }
        if (slices.Count == 0) {
            slices.Add(new FlightRatioSlice(false, 0));
        }
        return slices;
    }
}
