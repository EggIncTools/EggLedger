using EggLedger.Domain.MissionPacking;

namespace EggLedger.Web.Missions.Timeline;

public static class TimelineFlightRatio {
    public const int AssumedConcurrency = 3;

    public static double? Compute(
        IReadOnlyList<DatabaseMission> missions,
        DateTimeOffset visibleStart,
        DateTimeOffset visibleEnd,
        DateTimeOffset now) {
        long winStart = visibleStart.ToUnixTimeSeconds();
        long winEnd = Math.Min(visibleEnd.ToUnixTimeSeconds(), now.ToUnixTimeSeconds());
        if (winEnd <= winStart) {
            return null;
        }

        long flown = 0;
        foreach (var m in missions) {
            long start = Math.Max(m.LaunchDT, winStart);
            long end = Math.Min(m.ReturnDT, winEnd);
            if (end > start) {
                flown += end - start;
            }
        }

        return (double)flown / (AssumedConcurrency * (winEnd - winStart));
    }
}
