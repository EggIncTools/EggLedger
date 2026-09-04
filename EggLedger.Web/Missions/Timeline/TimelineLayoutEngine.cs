using EggIdentity.UI;
using EggLedger.Domain.MissionPacking;
using EggLedger.Web.Services;
using EggLedger.Web.State;

namespace EggLedger.Web.Missions.Timeline;

public static class TimelineLayoutEngine {
    private const double EventLaneGapFraction = 0.0001;

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

        var intersecting = IntersectingMissions(missions, windowStart, windowEnd);
        var laneRights = new List<double>();
        var result = new List<TimelineBar>(intersecting.Count);
        double iconGapFraction = minWidthPercent / 100;

        for (int i = 0; i < intersecting.Count; i++) {
            var m = intersecting[i];
            var (left, width, packLeft, packRight) = ClipToWindow(m.LaunchDT, m.ReturnDT, windowStart, windowSpan, minWidthPercent / 100);
            int lane = CalendarLanePacker.AssignLane(laneRights, packLeft, packRight, iconGapFraction);
            bool isActive = nowUnix < m.ReturnDT;
            double fill = FillFraction(m.LaunchDT, m.ReturnDT, windowStart, windowEnd, nowUnix, isActive);
            long progress = isActive ? Math.Min(nowUnix, m.ReturnDT) : m.ReturnDT;

            result.Add(new TimelineBar(
                MissionId: m.MissiondId,
                Lane: lane,
                StackOrder: i,
                LeftPercent: left * 100,
                WidthPercent: width * 100,
                FillPercent: fill * 100,
                IsActive: isActive,
                ShipIconPath: ContentPaths.Asset($"images/ships/{m.ShipEnumString}.png"),
                ShipName: m.ShipString,
                DurationIndex: m.DurationType is { } duration ? (int)duration : 3,
                TargetIconPath: TargetImagePaths.Resolve(m.Target).Path,
                Mission: m,
                HasData: noDataIds is null || !noDataIds.Contains(m.MissiondId),
                ShowBubble: progress > windowStart && progress <= windowEnd,
                ContinuesLeft: m.LaunchDT < windowStart,
                ContinuesRight: m.ReturnDT > windowEnd));
        }

        return result;
    }

    public static IReadOnlyList<TimelineEventBar> LayoutEvents(
        IReadOnlyList<GameEvent> events,
        DateTimeOffset visibleStart,
        DateTimeOffset visibleEnd) {
        long windowStart = visibleStart.ToUnixTimeSeconds();
        long windowEnd = visibleEnd.ToUnixTimeSeconds();
        double windowSpan = windowEnd - windowStart;

        var intersecting = new List<GameEvent>();
        foreach (var e in events) {
            if (e.EndTimestamp > windowStart && e.StartTimestamp < windowEnd) {
                intersecting.Add(e);
            }
        }
        intersecting.Sort((a, b) => a.StartTimestamp.CompareTo(b.StartTimestamp));

        var laneRights = new List<double>();
        var result = new List<TimelineEventBar>(intersecting.Count);
        foreach (var e in intersecting) {
            long start = (long)e.StartTimestamp;
            long end = (long)e.EndTimestamp;
            var (left, width, packLeft, packRight) = ClipToWindow(start, end, windowStart, windowSpan, 0);
            int lane = CalendarLanePacker.AssignLane(laneRights, packLeft, packRight, EventLaneGapFraction);
            result.Add(new TimelineEventBar(
                Id: e.Id,
                Lane: lane,
                Type: e.Type,
                Message: e.Message,
                Multiplier: e.Multiplier,
                Ultra: e.Ultra,
                StartUnix: start,
                EndUnix: end,
                LeftPercent: left * 100,
                WidthPercent: width * 100,
                ContinuesLeft: start < windowStart,
                ContinuesRight: end > windowEnd));
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

    private static (double Left, double Width, double PackLeft, double PackRight) ClipToWindow(
        long launchDt, long returnDt, long windowStart, double windowSpan, double minWidthFraction) {
        if (windowSpan <= 0) {
            return (0, 1, 0, 1);
        }
        double packLeft = (launchDt - windowStart) / windowSpan;
        double packRight = (returnDt - windowStart) / windowSpan;
        double left = Math.Max(0, packLeft);
        double right = Math.Min(1, packRight);
        double width = Math.Max(0, right - left);
        if (minWidthFraction > 0 && width < minWidthFraction) {
            width = Math.Min(minWidthFraction, 1);
            if (left + width > 1) {
                left = 1 - width;
            }
        }
        return (left, width, packLeft, packRight);
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
