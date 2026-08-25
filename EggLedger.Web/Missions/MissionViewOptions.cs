using EggLedger.Domain.MissionPacking;

namespace EggLedger.Web.Missions;

public enum MultiViewMode {
    Off,
    Row,
    Free
}

public enum MissionSortMethod {
    Default,
    Iv
}

public sealed class MissionViewOptions {

    public const string KeyViewByDate = "mission_view_by_date";
    public const string KeyViewTimes = "mission_view_times";
    public const string KeyMultiViewMode = "mission_multi_view_mode";
    public const string KeySortMethod = "mission_sort_method";
    public const string KeyCardPresets = "mission_card_presets";
    public const string KeySortByDropCount = "mission_sort_by_drop_count";
    public const string KeyCalendarFlightRatio = "calendar_flight_ratio";


    public bool ViewByDate { get; set; }
    public bool ViewMissionTimes { get; set; } = true;
    public MultiViewMode MultiViewMode { get; set; } = MultiViewMode.Off;
    public MissionSortMethod SortMethod { get; set; } = MissionSortMethod.Default;
    public bool SortByDropCount { get; set; }
    public bool CalendarFlightRatio { get; set; }

    public int? MissionTypeTab { get; set; }

    public static bool HasBothMissionTypes(IReadOnlyList<DatabaseMission>? missions) {
        if (missions is null || missions.Count == 0) {
            return false;
        }
        bool home = false;
        bool virtue = false;
        foreach (var m in missions) {
            if (m.MissionType == 0) {
                home = true;
            } else if (m.MissionType == 1) {
                virtue = true;
            }
        }

        return home && virtue;
    }

    public static IReadOnlyList<DatabaseMission>? TabFilteredMissions(
        IReadOnlyList<DatabaseMission>? filteredMissions,
        int? missionTypeTab) {
        if (missionTypeTab is null || filteredMissions is null) {
            return filteredMissions;
        }
        var result = new List<DatabaseMission>();
        foreach (var m in filteredMissions) {
            if (m.MissionType == missionTypeTab.Value) {
                result.Add(m);
            }
        }
        return result;
    }

    public static MultiViewMode ParseMultiViewMode(string? raw) => raw switch {
        "row" => MultiViewMode.Row,
        "free" => MultiViewMode.Free,
        _ => MultiViewMode.Off
    };

    public static string MultiViewModeToString(MultiViewMode mode) => mode switch {
        MultiViewMode.Row => "row",
        MultiViewMode.Free => "free",
        _ => "off"
    };

    public static MissionSortMethod ParseSortMethod(string? raw) =>
        raw == "iv" ? MissionSortMethod.Iv : MissionSortMethod.Default;

    public static string SortMethodToString(MissionSortMethod method) =>
        method == MissionSortMethod.Iv ? "iv" : "default";

    public void LoadFrom(IReadOnlyDictionary<string, string> settings) {
        if (settings.TryGetValue(KeyViewByDate, out var vbd)) {
            ViewByDate = ParseBool(vbd, ViewByDate);
        }
        if (settings.TryGetValue(KeyViewTimes, out var vt)) {
            ViewMissionTimes = ParseBool(vt, ViewMissionTimes);
        }
        if (settings.TryGetValue(KeyMultiViewMode, out var mvm)) {
            MultiViewMode = ParseMultiViewMode(mvm);
        }
        if (settings.TryGetValue(KeySortMethod, out var sm)) {
            SortMethod = ParseSortMethod(sm);
        }
        if (settings.TryGetValue(KeySortByDropCount, out var sdc)) {
            SortByDropCount = ParseBool(sdc, SortByDropCount);
        }
        if (settings.TryGetValue(KeyCalendarFlightRatio, out var cfr)) {
            CalendarFlightRatio = ParseBool(cfr, CalendarFlightRatio);
        }
    }

    private static bool ParseBool(string raw, bool fallback) =>
        bool.TryParse(raw, out var v) ? v : fallback;
}
