using System.Globalization;

namespace EggLedger.Web.Missions.Timeline;

public sealed record TimelineTick(double LeftPercent, string Label, bool HasLine);

public sealed record TimelineDayCell(double LeftPercent, double WidthPercent, DateTime LocalDate, bool Muted);

public static class TimelineTicks {
    public static IReadOnlyList<TimelineTick> HourTicks(DateTimeOffset start, DateTimeOffset end, TimeZoneInfo tz) {
        double spanSeconds = (end - start).TotalSeconds;
        if (spanSeconds <= 0) {
            return [];
        }
        var localStart = TimeZoneInfo.ConvertTime(start, tz).DateTime;
        var first = localStart.Minute == 0 && localStart.Second == 0
            ? localStart
            : new DateTime(localStart.Year, localStart.Month, localStart.Day, localStart.Hour, 0, 0, DateTimeKind.Unspecified).AddHours(1);
        var result = new List<TimelineTick>();
        for (var local = first; Percent(local, start, spanSeconds, tz) < 100; local = local.AddHours(1)) {
            double pos = Percent(local, start, spanSeconds, tz);
            if (pos <= 0) {
                continue;
            }
            bool labeled = local.Hour % 3 == 0;
            result.Add(new TimelineTick(pos, labeled ? local.ToString("h tt", CultureInfo.InvariantCulture) : "", true));
        }
        return result;
    }

    public static IReadOnlyList<TimelineDayCell> DayCells(DateTimeOffset start, DateTimeOffset end, int? primaryMonth) {
        double spanSeconds = (end - start).TotalSeconds;
        if (spanSeconds <= 0) {
            return [];
        }
        var cells = new List<TimelineDayCell>();
        for (var day = start; day < end; day = day.AddDays(1)) {
            double left = Math.Max(0, Percent(day, start, spanSeconds));
            double right = Math.Min(100, Percent(day.AddDays(1), start, spanSeconds));
            var easternDate = TimelineGridAnchor.EasternDate(day);
            bool muted = primaryMonth is { } month && easternDate.Month != month;
            cells.Add(new TimelineDayCell(left, right - left, easternDate, muted));
        }
        return cells;
    }

    private static double Percent(DateTimeOffset instant, DateTimeOffset start, double spanSeconds) =>
        (instant - start).TotalSeconds / spanSeconds * 100;

    private static double Percent(DateTime local, DateTimeOffset start, double spanSeconds, TimeZoneInfo tz) =>
        (TimelineWindow.ToOffset(local, tz) - start).TotalSeconds / spanSeconds * 100;
}
