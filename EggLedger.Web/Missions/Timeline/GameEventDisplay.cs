using System.Collections.Frozen;

namespace EggLedger.Web.Missions.Timeline;

public static class GameEventDisplay {
    private static readonly FrozenDictionary<string, string> ColorByType = new Dictionary<string, string> {
        ["mission-fuel"] = "#8b5cf6",
        ["mission-capacity"] = "#a21caf",
        ["mission-duration"] = "#be185d"
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    public static string Slug(string type) => type.Replace('_', '-').ToLowerInvariant();

    public static string Title(string type, string? message) => Slug(type) switch {
        "mission-capacity" => "Double capacity",
        "mission-duration" => "Shorter missions",
        "mission-fuel" => "Fuel bonus",
        _ => message ?? type
    };

    public static readonly IReadOnlyList<string> CalendarTypes = ["mission-capacity", "mission-duration"];

    public static string Color(string type) => ColorByType.GetValueOrDefault(type, "#9ca3af");

    public static string Chance(double confidence) =>
        Math.Round(confidence * 100).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "%";
}
