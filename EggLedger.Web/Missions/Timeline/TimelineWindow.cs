namespace EggLedger.Web.Missions.Timeline;

public static class TimelineWindow {
    public static (DateTimeOffset Start, DateTimeOffset End) Compute(DateTimeOffset center, TimelineZoom zoom, TimeZoneInfo tz) {
        var local = TimeZoneInfo.ConvertTime(center, tz).DateTime;
        var (startLocal, endLocal) = zoom switch {
            TimelineZoom.Hour => HourBlock(local),
            TimelineZoom.Week => (WeekStart(local), WeekStart(local).AddDays(7)),
            TimelineZoom.Month => (MonthStart(local), MonthStart(local).AddMonths(1)),
            _ => (local.Date, local.Date.AddDays(1)),
        };
        return (ToOffset(startLocal, tz), ToOffset(endLocal, tz));
    }

    public static DateTimeOffset ToOffset(DateTime local, TimeZoneInfo tz) =>
        new(local, tz.GetUtcOffset(local));

    private static (DateTime Start, DateTime End) HourBlock(DateTime local) {
        var start = new DateTime(local.Year, local.Month, local.Day, local.Hour, 0, 0, DateTimeKind.Unspecified).AddHours(-3);
        return (start, start.AddHours(6));
    }

    private static DateTime WeekStart(DateTime local) => local.Date.AddDays(-(int)local.DayOfWeek);

    private static DateTime MonthStart(DateTime local) => new(local.Year, local.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
}
