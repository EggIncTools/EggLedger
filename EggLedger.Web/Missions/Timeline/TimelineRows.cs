namespace EggLedger.Web.Missions.Timeline;

public sealed record TimelineRow(DateTimeOffset Start, DateTimeOffset End, bool IsPrimary);

public static class TimelineRows {
    public static IReadOnlyList<TimelineRow> Build(TimelineZoom zoom, DateTimeOffset visibleStart, DateTimeOffset visibleEnd) {
        return zoom switch {
            TimelineZoom.Month => MonthRows(visibleStart, visibleEnd),
            _ => [new TimelineRow(visibleStart, visibleEnd, true)],
        };
    }

    private static List<TimelineRow> MonthRows(DateTimeOffset visibleStart, DateTimeOffset visibleEnd) {
        var monthStartDate = DateOnly.FromDateTime(visibleStart.DateTime);
        var firstRowDate = TimelineGridAnchor.GridWeekStartDate(monthStartDate);
        var rows = new List<TimelineRow>();
        for (var rowDate = firstRowDate; TimelineGridAnchor.GridDayStartForDate(rowDate) < visibleEnd; rowDate = rowDate.AddDays(7)) {
            rows.Add(new TimelineRow(TimelineGridAnchor.GridDayStartForDate(rowDate), TimelineGridAnchor.GridDayStartForDate(rowDate.AddDays(7)), true));
        }
        return rows;
    }
}
