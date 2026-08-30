using EggIdentity.UI;

namespace EggLedger.Web.Missions.Timeline;

public static class TimelineGridAnchor {
    private static readonly TimeZoneInfo Eastern = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
    private static readonly TimeSpan Noon = TimeSpan.FromHours(12);

    public static DateTimeOffset GridDayStart(DateTimeOffset instant) => CalendarGridAnchor.DayStart(instant, Eastern, Noon);

    public static DateTimeOffset GridWeekStart(DateTimeOffset instant) => CalendarGridAnchor.WeekStart(instant, Eastern, Noon);

    public static DateTime EasternDate(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, Eastern).Date;

    public static DateTimeOffset GridDayStartForDate(DateOnly date) => CalendarGridAnchor.DayStartForDate(date, Eastern, Noon);

    public static DateTimeOffset GridWeekStartForDate(DateOnly date) => CalendarGridAnchor.WeekStartForDate(date, Eastern, Noon);

    public static DateOnly GridWeekStartDate(DateOnly date) => CalendarGridAnchor.WeekStartDate(date);
}
