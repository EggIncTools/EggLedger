using EggLedger.Domain.MissionPacking;
using EggLedger.Web.State;

namespace EggLedger.Web.Missions.Timeline;

public static class TimelineLayoutEngine {
    public static IReadOnlyList<TimelineBar> Layout(
        IReadOnlyList<DatabaseMission> missions,
        DateTimeOffset visibleStart,
        DateTimeOffset visibleEnd,
        DateTimeOffset now) {
        long windowStart = visibleStart.ToUnixTimeSeconds();
        long windowEnd = visibleEnd.ToUnixTimeSeconds();
        long nowUnix = now.ToUnixTimeSeconds();
        double windowSpan = windowEnd - windowStart;

        var intersecting = IntersectingMissions(missions, windowStart, windowEnd);
        var laneEnds = new List<long>();
        var result = new List<TimelineBar>(intersecting.Count);

        foreach (var m in intersecting) {
            int lane = AssignLane(laneEnds, m.LaunchDT, m.ReturnDT);
            var (left, width) = ClipToWindow(m.LaunchDT, m.ReturnDT, windowStart, windowSpan);
            bool isActive = nowUnix < m.ReturnDT;
            double fill = FillFraction(m.LaunchDT, m.ReturnDT, windowStart, windowEnd, nowUnix, isActive);

            result.Add(new TimelineBar(
                MissionId: m.MissiondId,
                Lane: lane,
                LeftPercent: left * 100,
                WidthPercent: width * 100,
                FillPercent: fill * 100,
                IsActive: isActive,
                ShipIconPath: ContentPaths.Asset($"images/ships/{m.ShipEnumString}.png"),
                ShipName: m.ShipString,
                DurationIndex: m.DurationType is { } duration ? (int)duration : 3,
                TargetIconPath: TargetImagePaths.Resolve(m.Target).Path,
                Mission: m));
        }

        return result;
    }

    private static List<DatabaseMission> IntersectingMissions(
        IReadOnlyList<DatabaseMission> missions, long windowStart, long windowEnd) {
        var intersecting = new List<DatabaseMission>();
        foreach (var m in missions) {
            if (m.ReturnDT > windowStart && m.LaunchDT < windowEnd) {
                intersecting.Add(m);
            }
        }
        intersecting.Sort((a, b) => a.LaunchDT.CompareTo(b.LaunchDT));
        return intersecting;
    }

    private static int AssignLane(List<long> laneEnds, long launchDt, long returnDt) {
        for (int i = 0; i < laneEnds.Count; i++) {
            if (laneEnds[i] <= launchDt) {
                laneEnds[i] = returnDt;
                return i;
            }
        }
        laneEnds.Add(returnDt);
        return laneEnds.Count - 1;
    }

    private static (double Left, double Width) ClipToWindow(
        long launchDt, long returnDt, long windowStart, double windowSpan) {
        if (windowSpan <= 0) {
            return (0, 1);
        }
        double left = Math.Max(0, (launchDt - windowStart) / windowSpan);
        double right = Math.Min(1, (returnDt - windowStart) / windowSpan);
        return (left, Math.Max(0, right - left));
    }

    private static double FillFraction(long launchDt, long returnDt, long windowStart, long windowEnd, long nowUnix, bool isActive) {
        if (!isActive) {
            return 1;
        }
        long clipStart = Math.Max(launchDt, windowStart);
        long clipEnd = Math.Min(returnDt, windowEnd);
        if (clipEnd <= clipStart) {
            return 0;
        }
        return Math.Clamp((double)(nowUnix - clipStart) / (clipEnd - clipStart), 0, 1);
    }
}
