namespace EggLedger.Web.Missions.Timeline;

public static class TimelineWindow {
    public static (DateTimeOffset Start, DateTimeOffset End) Compute(DateTimeOffset center, TimelineZoom zoom, TimeZoneInfo tz) {
        if (zoom is TimelineZoom.Week or TimelineZoom.Month) {
            var weekStart = TimelineGridAnchor.GridWeekStart(center);
            return (weekStart, weekStart.AddDays(7));
        }
        var local = TimeZoneInfo.ConvertTime(center, tz).DateTime;
        return (ToOffset(local.Date, tz), ToOffset(local.Date.AddDays(1), tz));
    }

    public static DateTimeOffset ToOffset(DateTime local, TimeZoneInfo tz) =>
        new(local, tz.GetUtcOffset(local));
}
