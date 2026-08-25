namespace EggLedger.Web.Missions.Timeline;

public enum TimelineZoom {
    Hour,
    Day,
    Week,
    Month,
}

public static class TimelineZoomExtensions {
    public static TimeSpan WindowSpan(this TimelineZoom zoom) => zoom switch {
        TimelineZoom.Hour => TimeSpan.FromHours(6),
        TimelineZoom.Day => TimeSpan.FromDays(1),
        TimelineZoom.Week => TimeSpan.FromDays(7),
        TimelineZoom.Month => TimeSpan.FromDays(30),
        _ => TimeSpan.FromDays(1),
    };

    public static string Label(this TimelineZoom zoom) => zoom switch {
        TimelineZoom.Hour => "Hour",
        TimelineZoom.Day => "Day",
        TimelineZoom.Week => "Week",
        TimelineZoom.Month => "Month",
        _ => "Day",
    };
}
