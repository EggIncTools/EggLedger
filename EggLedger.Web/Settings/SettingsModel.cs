using System.Globalization;

namespace EggLedger.Web.Settings;

public sealed class SettingsModel {


    public const string KeyAutoRefreshMenno = "auto_refresh_menno_pref";
    public const string KeyLastMennoRefresh = "last_menno_data_refresh_at";
    public const string KeyWorkerCount = "worker_count";
    public const string KeyScreenshotSafety = "screenshot_safety";
    public const string KeyShowMissionProgress = "show_mission_progress";
    public const string KeyAdvancedDropFilter = "advanced_drop_filter";
    public const string KeyAutoExportCsv = "auto_export_csv";
    public const string KeyAutoExportXlsx = "auto_export_xlsx";
    public const string KeyWorkerCountWarningRead = "worker_count_warning_read";
    public const string KeyWindowWidth = "resolution_width";
    public const string KeyWindowHeight = "resolution_height";
    public const string KeyStartInFullscreen = "resolution_start_fullscreen";
    public const string KeyExportKeepCount = "export_keep_count";
    public const string KeyStorageFolderHidden = "storage_folder_hidden";
    public const string KeyBackupDestPath = "backup_dest_path";
    public const string KeyMoveDestPath = "move_dest_path";



    public const int MinWorkerCount = 1;
    public const int MaxWorkerCount = 10;

    public const int DefaultWindowWidth = 1280;
    public const int DefaultWindowHeight = 800;

    public bool AutoRefreshMenno { get; set; }

    public DateTimeOffset? LastMennoRefreshAt { get; set; }

    public int WorkerCount { get; set; } = MinWorkerCount;

    public bool ScreenshotSafety { get; set; }

    public bool ShowMissionProgress { get; set; } = true;

    public bool AdvancedDropFilter { get; set; }

    public bool AutoExportCsv { get; set; } = true;

    public bool AutoExportXlsx { get; set; } = true;

    public bool WorkerCountWarningRead { get; set; }

    public int WindowWidth { get; set; }

    public int WindowHeight { get; set; }

    public bool StartInFullscreen { get; set; }

    public int ExportKeepCount { get; set; }

    public bool StorageFolderHidden { get; set; }

    public string BackupDestPath { get; set; } = "";

    public string MoveDestPath { get; set; } = "";

    public static int ClampWorkerCount(int n) => n < MinWorkerCount ? MinWorkerCount
        : n > MaxWorkerCount ? MaxWorkerCount
        : n;

    public void LoadFrom(IReadOnlyDictionary<string, string> settings) {
        AutoRefreshMenno = SettingsDictionaryParsing.Bool(settings, KeyAutoRefreshMenno, AutoRefreshMenno);
        if (settings.TryGetValue(KeyLastMennoRefresh, out var lmr)
            && DateTimeOffset.TryParse(lmr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var lmrParsed)) {
            LastMennoRefreshAt = lmrParsed;
        }
        WorkerCount = ClampWorkerCount(SettingsDictionaryParsing.Int(settings, KeyWorkerCount, WorkerCount));
        ScreenshotSafety = SettingsDictionaryParsing.Bool(settings, KeyScreenshotSafety, ScreenshotSafety);
        ShowMissionProgress = SettingsDictionaryParsing.Bool(settings, KeyShowMissionProgress, ShowMissionProgress);
        AdvancedDropFilter = SettingsDictionaryParsing.Bool(settings, KeyAdvancedDropFilter, AdvancedDropFilter);
        AutoExportCsv = SettingsDictionaryParsing.Bool(settings, KeyAutoExportCsv, AutoExportCsv);
        AutoExportXlsx = SettingsDictionaryParsing.Bool(settings, KeyAutoExportXlsx, AutoExportXlsx);
        WorkerCountWarningRead = SettingsDictionaryParsing.Bool(settings, KeyWorkerCountWarningRead, WorkerCountWarningRead);
        WindowWidth = SettingsDictionaryParsing.Int(settings, KeyWindowWidth, WindowWidth);
        WindowHeight = SettingsDictionaryParsing.Int(settings, KeyWindowHeight, WindowHeight);
        StartInFullscreen = SettingsDictionaryParsing.Bool(settings, KeyStartInFullscreen, StartInFullscreen);
        ExportKeepCount = SettingsDictionaryParsing.Int(settings, KeyExportKeepCount, ExportKeepCount);
        StorageFolderHidden = SettingsDictionaryParsing.Bool(settings, KeyStorageFolderHidden, StorageFolderHidden);
        BackupDestPath = SettingsDictionaryParsing.Str(settings, KeyBackupDestPath, BackupDestPath);
        MoveDestPath = SettingsDictionaryParsing.Str(settings, KeyMoveDestPath, MoveDestPath);
    }

    public static string FormatBool(bool v) => v ? "true" : "false";

    public static string FormatInt(int v) => v.ToString(CultureInfo.InvariantCulture);
}
