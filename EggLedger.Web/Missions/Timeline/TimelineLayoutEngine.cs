using EggLedger.Domain.MissionPacking;
using EggLedger.Web.State;

namespace EggLedger.Web.Missions.Timeline;

public static class TimelineLayoutEngine {
    public static IReadOnlyList<TimelineBar> Layout(
        IReadOnlyList<DatabaseMission> missions,
        DateTimeOffset visibleStart,
        DateTimeOffset visibleEnd,
        DateTimeOffset now,
        double minWidthPercent = 0,
        ISet<string>? noDataIds = null) {
        long windowStart = visibleStart.ToUnixTimeSeconds();
        long windowEnd = visibleEnd.ToUnixTimeSeconds();
        long nowUnix = now.ToUnixTimeSeconds();
        double windowSpan = windowEnd - windowStart;
        long minSpanSeconds = (long)(windowSpan * minWidthPercent / 100);

        var intersecting = IntersectingMissions(missions, windowStart, windowEnd);
        var laneEnds = new List<long>();
        var result = new List<TimelineBar>(intersecting.Count);

        foreach (var m in intersecting) {
            long visualReturn = Math.Max(m.ReturnDT, m.LaunchDT + minSpanSeconds);
            int lane = AssignLane(laneEnds, m.LaunchDT, visualReturn);
            var (left, width) = ClipToWindow(m.LaunchDT, visualReturn, windowStart, windowSpan, minWidthPercent / 100);
            bool isActive = nowUnix < m.ReturnDT;
            double fill = FillFraction(m.LaunchDT, visualReturn, windowStart, windowEnd, nowUnix, isActive);

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
                Mission: m,
                HasData: noDataIds is null || !noDataIds.Contains(m.MissiondId)));
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
        long launchDt, long returnDt, long windowStart, double windowSpan, double minWidthFraction) {
        if (windowSpan <= 0) {
            return (0, 1);
        }
        double left = Math.Max(0, (launchDt - windowStart) / windowSpan);
        double right = Math.Min(1, (returnDt - windowStart) / windowSpan);
        double width = Math.Max(0, right - left);
        if (minWidthFraction > 0 && width < minWidthFraction) {
            width = Math.Min(minWidthFraction, 1);
            if (left + width > 1) {
                left = 1 - width;
            }
        }
        return (left, width);
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
