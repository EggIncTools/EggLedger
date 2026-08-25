using System.Globalization;

namespace EggLedger.Web.Missions.Timeline;

public sealed record TimelineTick(double LeftPercent, string Label, bool IsMajor, bool HasLine);

public static class TimelineTicks {
    public static IReadOnlyList<TimelineTick> Generate(DateTimeOffset start, DateTimeOffset end, TimelineZoom zoom, TimeZoneInfo tz) {
        double spanSeconds = (end - start).TotalSeconds;
        if (spanSeconds <= 0) {
            return [];
        }
        var localStart = TimeZoneInfo.ConvertTime(start, tz).DateTime;
        return zoom is TimelineZoom.Hour or TimelineZoom.Day
            ? HourTicks(localStart, start, spanSeconds, zoom, tz)
            : DayTicks(localStart, start, spanSeconds, zoom, tz);
    }

    private static List<TimelineTick> HourTicks(DateTime localStart, DateTimeOffset start, double spanSeconds, TimelineZoom zoom, TimeZoneInfo tz) {
        var result = new List<TimelineTick>();
        var first = localStart.Minute == 0 && localStart.Second == 0
            ? localStart
            : new DateTime(localStart.Year, localStart.Month, localStart.Day, localStart.Hour, 0, 0, DateTimeKind.Unspecified).AddHours(1);
        for (var local = first; Percent(local, start, spanSeconds, tz) < 100; local = local.AddHours(1)) {
            double pos = Percent(local, start, spanSeconds, tz);
            if (pos <= 0) {
                continue;
            }
            bool labeled = zoom == TimelineZoom.Hour || local.Hour % 3 == 0;
            result.Add(new TimelineTick(pos, labeled ? local.ToString("h tt", CultureInfo.InvariantCulture) : "", labeled, true));
        }
        return result;
    }

    private static List<TimelineTick> DayTicks(DateTime localStart, DateTimeOffset start, double spanSeconds, TimelineZoom zoom, TimeZoneInfo tz) {
        var result = new List<TimelineTick>();
        for (var day = localStart.Date; Percent(day, start, spanSeconds, tz) < 100; day = day.AddDays(1)) {
            double linePos = Percent(day, start, spanSeconds, tz);
            if (linePos > 0) {
                result.Add(new TimelineTick(linePos, "", zoom == TimelineZoom.Week || day.Day == 1, true));
            }
            bool labeled = zoom == TimelineZoom.Week || day.Day % 2 == 1;
            if (!labeled) {
                continue;
            }
            double centerPos = Percent(day.AddHours(12), start, spanSeconds, tz);
            if (centerPos is > 0 and < 100) {
                string label = zoom == TimelineZoom.Week
                    ? day.ToString("ddd M/d", CultureInfo.InvariantCulture)
                    : day.ToString("M/d", CultureInfo.InvariantCulture);
                result.Add(new TimelineTick(centerPos, label, false, false));
            }
        }
        return result;
    }

    private static double Percent(DateTime local, DateTimeOffset start, double spanSeconds, TimeZoneInfo tz) =>
        (TimelineWindow.ToOffset(local, tz) - start).TotalSeconds / spanSeconds * 100;
}
