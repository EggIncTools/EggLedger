using System.Text.Json.Serialization;
using EggLedger.Domain.MissionQuery;
using EggLedger.Domain.Reports;
using EggLedger.Web.Data;

namespace EggLedger.Web.Settings;

public sealed record CloudSyncableSettings {
    [JsonPropertyName("auto_refresh_menno_pref")]
    public bool AutoRefreshMennoPref { get; init; }
    [JsonPropertyName("worker_count")]
    public int WorkerCount { get; init; }
    [JsonPropertyName("screenshot_safety")]
    public bool ScreenshotSafety { get; init; }
    [JsonPropertyName("show_mission_progress")]
    public bool ShowMissionProgress { get; init; }
    [JsonPropertyName("advanced_drop_filter")]
    public bool AdvancedDropFilter { get; init; }
    [JsonPropertyName("mission_view_by_date")]
    public bool MissionViewByDate { get; init; }
    [JsonPropertyName("mission_view_times")]
    public bool MissionViewTimes { get; init; }
    [JsonPropertyName("mission_recolor_dc")]
    public bool MissionRecolorDC { get; init; }
    [JsonPropertyName("mission_recolor_bc")]
    public bool MissionRecolorBC { get; init; }
    [JsonPropertyName("mission_show_expected_drops")]
    public bool MissionShowExpectedDrops { get; init; }
    [JsonPropertyName("mission_multi_view_mode")]
    public string MissionMultiViewMode { get; init; } = "";
    [JsonPropertyName("mission_sort_method")]
    public string MissionSortMethod { get; init; } = "";
    [JsonPropertyName("lifetime_sort_method")]
    public string LifetimeSortMethod { get; init; } = "";
    [JsonPropertyName("lifetime_show_drops_per_ship")]
    public bool LifetimeShowDropsPerShip { get; init; }
    [JsonPropertyName("lifetime_show_expected_totals")]
    public bool LifetimeShowExpectedTotals { get; init; }
}

public sealed record CloudReportGroup {
    [JsonPropertyName("Id")]
    public string Id { get; init; } = "";
    [JsonPropertyName("AccountId")]
    public string AccountId { get; init; } = "";
    [JsonPropertyName("Name")]
    public string Name { get; init; } = "";
    [JsonPropertyName("SortOrder")]
    public int SortOrder { get; init; }
    [JsonPropertyName("CreatedAt")]
    public long CreatedAt { get; init; }

    public static CloudReportGroup FromRow(ReportGroupRow r) => new() {
        Id = r.Id,
        AccountId = r.AccountId,
        Name = r.Name,
        SortOrder = r.SortOrder,
        CreatedAt = r.CreatedAt,
    };

    public ReportGroupRow ToRow() => new() {
        Id = Id,
        AccountId = AccountId,
        Name = Name,
        SortOrder = SortOrder,
        CreatedAt = CreatedAt,
    };
}

public sealed record CloudReportsBlob {
    [JsonPropertyName("reports")]
    public IReadOnlyList<ReportDefinition> Reports { get; init; } = [];
    [JsonPropertyName("groups")]
    public IReadOnlyList<CloudReportGroup> Groups { get; init; } = [];

    public static CloudReportsBlob Pack(
        IReadOnlyList<ReportRow> reports, IReadOnlyList<ReportGroupRow> groups) => new() {
            Reports = reports.Select(ReportMapping.ToDefinition).ToList(),
            Groups = groups.Select(CloudReportGroup.FromRow).ToList(),
        };
}

public static class CloudSyncBlobs {
    public const string AccountsBlob = "accounts";
    public const string SettingsBlob = "settings";
    public const string ReportsBlob = "reports";

    public static CloudSyncableSettings PackSettings(IReadOnlyDictionary<string, string> settings) => new() {
        AutoRefreshMennoPref = SettingsDictionaryParsing.Bool(settings, SettingsModel.KeyAutoRefreshMenno, false),
        WorkerCount = SettingsModel.ClampWorkerCount(SettingsDictionaryParsing.Int(settings, SettingsModel.KeyWorkerCount, SettingsModel.MinWorkerCount)),
        ScreenshotSafety = SettingsDictionaryParsing.Bool(settings, SettingsModel.KeyScreenshotSafety, false),
        ShowMissionProgress = SettingsDictionaryParsing.Bool(settings, SettingsModel.KeyShowMissionProgress, true),
        AdvancedDropFilter = SettingsDictionaryParsing.Bool(settings, SettingsModel.KeyAdvancedDropFilter, false),
        MissionViewByDate = SettingsDictionaryParsing.Bool(settings, "mission_view_by_date", false),
        MissionViewTimes = SettingsDictionaryParsing.Bool(settings, "mission_view_times", true),
        MissionRecolorDC = SettingsDictionaryParsing.Bool(settings, "mission_recolor_dc", false),
        MissionRecolorBC = SettingsDictionaryParsing.Bool(settings, "mission_recolor_bc", false),
        MissionShowExpectedDrops = SettingsDictionaryParsing.Bool(settings, "mission_show_expected_drops", true),
        MissionMultiViewMode = SettingsDictionaryParsing.Str(settings, "mission_multi_view_mode", "off"),
        MissionSortMethod = SettingsDictionaryParsing.Str(settings, "mission_sort_method", "default"),
        LifetimeSortMethod = SettingsDictionaryParsing.Str(settings, "lifetime_sort_method", ""),
        LifetimeShowDropsPerShip = SettingsDictionaryParsing.Bool(settings, "lifetime_show_drops_per_ship", false),
        LifetimeShowExpectedTotals = SettingsDictionaryParsing.Bool(settings, "lifetime_show_expected_totals", false),
    };

    public static IReadOnlyDictionary<string, string> UnpackSettings(CloudSyncableSettings s) => new Dictionary<string, string> {
        [SettingsModel.KeyAutoRefreshMenno] = SettingsModel.FormatBool(s.AutoRefreshMennoPref),
        [SettingsModel.KeyWorkerCount] = SettingsModel.FormatInt(SettingsModel.ClampWorkerCount(s.WorkerCount)),
        [SettingsModel.KeyScreenshotSafety] = SettingsModel.FormatBool(s.ScreenshotSafety),
        [SettingsModel.KeyShowMissionProgress] = SettingsModel.FormatBool(s.ShowMissionProgress),
        [SettingsModel.KeyAdvancedDropFilter] = SettingsModel.FormatBool(s.AdvancedDropFilter),
        ["mission_view_by_date"] = SettingsModel.FormatBool(s.MissionViewByDate),
        ["mission_view_times"] = SettingsModel.FormatBool(s.MissionViewTimes),
        ["mission_recolor_dc"] = SettingsModel.FormatBool(s.MissionRecolorDC),
        ["mission_recolor_bc"] = SettingsModel.FormatBool(s.MissionRecolorBC),
        ["mission_show_expected_drops"] = SettingsModel.FormatBool(s.MissionShowExpectedDrops),
        ["mission_multi_view_mode"] = s.MissionMultiViewMode ?? "",
        ["mission_sort_method"] = s.MissionSortMethod ?? "",
        ["lifetime_sort_method"] = s.LifetimeSortMethod ?? "",
        ["lifetime_show_drops_per_ship"] = SettingsModel.FormatBool(s.LifetimeShowDropsPerShip),
        ["lifetime_show_expected_totals"] = SettingsModel.FormatBool(s.LifetimeShowExpectedTotals),
    };

    public static (List<ReportGroupRow> Groups, List<ReportRow> Reports) SelectReportsToImport(
        CloudReportsBlob remote,
        IReadOnlyCollection<string> existingGroupIds,
        IReadOnlyCollection<string> existingReportIds) {
        var groups = new List<ReportGroupRow>();
        var seenGroups = new HashSet<string>(existingGroupIds, StringComparer.Ordinal);
        foreach (var g in remote.Groups ?? []) {
            if (string.IsNullOrEmpty(g.Id) || !seenGroups.Add(g.Id)) {
                continue;
            }
            groups.Add(g.ToRow());
        }

        var reports = new List<ReportRow>();
        var seenReports = new HashSet<string>(existingReportIds, StringComparer.Ordinal);
        foreach (var d in remote.Reports ?? []) {
            if (string.IsNullOrEmpty(d.Id) || !seenReports.Add(d.Id)) {
                continue;
            }
            reports.Add(ReportMapping.ToRow(d));
        }

        return (groups, reports);
    }
}
