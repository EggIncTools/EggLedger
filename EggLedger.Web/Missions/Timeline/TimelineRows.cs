namespace EggLedger.Web.Missions.Timeline;

public sealed record TimelineRow(DateTimeOffset Start, DateTimeOffset End, bool IsPrimary);

public static class TimelineRows {
    public static IReadOnlyList<TimelineRow> Build(TimelineZoom zoom, DateTimeOffset visibleStart, DateTimeOffset visibleEnd, TimeZoneInfo tz) {
        return zoom switch {
            TimelineZoom.Month => MonthRows(visibleStart, visibleEnd, tz),
            _ => [new TimelineRow(visibleStart, visibleEnd, true)],
        };
    }

    private static List<TimelineRow> MonthRows(DateTimeOffset visibleStart, DateTimeOffset visibleEnd, TimeZoneInfo tz) {
        var localStart = TimeZoneInfo.ConvertTime(visibleStart, tz).DateTime;
        var firstRowDay = localStart.Date.AddDays(-(int)localStart.DayOfWeek);
        var rows = new List<TimelineRow>();
        for (var day = firstRowDay; TimelineWindow.ToOffset(day, tz) < visibleEnd; day = day.AddDays(7)) {
            rows.Add(new TimelineRow(TimelineWindow.ToOffset(day, tz), TimelineWindow.ToOffset(day.AddDays(7), tz), true));
        }
        return rows;
    }
}
