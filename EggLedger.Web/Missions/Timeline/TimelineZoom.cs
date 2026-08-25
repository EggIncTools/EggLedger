namespace EggLedger.Web.Missions.Timeline;

public enum TimelineZoom {
    Day,
    Week,
    Month,
}

public static class TimelineZoomExtensions {
    public static string Label(this TimelineZoom zoom) => zoom switch {
        TimelineZoom.Day => "Day",
        TimelineZoom.Week => "Week",
        TimelineZoom.Month => "Month",
        _ => "Day",
    };
}
