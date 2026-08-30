namespace EggLedger.Web.Missions.Timeline;

public static class TimelineWindow {
    public static (DateTimeOffset Start, DateTimeOffset End) Compute(DateTimeOffset center, TimelineZoom zoom, TimeZoneInfo tz) {
        var local = TimeZoneInfo.ConvertTime(center, tz).DateTime;
        if (zoom == TimelineZoom.Week) {
            var weekStart = TimelineGridAnchor.GridWeekStart(center);
            return (weekStart, weekStart.AddDays(7));
        }
        var (startLocal, endLocal) = zoom switch {
            TimelineZoom.Month => (MonthStart(local), MonthStart(local).AddMonths(1)),
            _ => (local.Date, local.Date.AddDays(1)),
        };
        return (ToOffset(startLocal, tz), ToOffset(endLocal, tz));
    }

    public static DateTimeOffset ToOffset(DateTime local, TimeZoneInfo tz) =>
        new(local, tz.GetUtcOffset(local));

    private static DateTime MonthStart(DateTime local) => new(local.Year, local.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
}
