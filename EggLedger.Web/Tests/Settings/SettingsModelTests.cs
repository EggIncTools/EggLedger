using EggLedger.Web.Settings;

namespace EggLedger.Web.Tests.Settings;

public sealed class SettingsModelTests {
    [Fact]
    public void Defaults_MatchGo() {
        var m = new SettingsModel();
        Assert.False(m.AutoRefreshMenno);
        Assert.Equal(1, m.WorkerCount);
        Assert.False(m.ScreenshotSafety);
        Assert.True(m.ShowMissionProgress);
        Assert.False(m.AdvancedDropFilter);
        Assert.True(m.AutoExportCsv);
        Assert.True(m.AutoExportXlsx);
        Assert.False(m.WorkerCountWarningRead);
        Assert.Equal(0, m.WindowWidth);
        Assert.Equal(0, m.WindowHeight);
        Assert.False(m.StartInFullscreen);
        Assert.Equal(0, m.ExportKeepCount);
        Assert.False(m.StorageFolderHidden);
        Assert.Equal("", m.BackupDestPath);
        Assert.Equal("", m.MoveDestPath);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(1, 1)]
    [InlineData(5, 5)]
    [InlineData(10, 10)]
    [InlineData(11, 10)]
    [InlineData(100, 10)]
    public void ClampWorkerCount_MatchesGoBounds(int input, int expected) =>
        Assert.Equal(expected, SettingsModel.ClampWorkerCount(input));

    [Fact]
    public void LoadFrom_EmptyMap_KeepsDefaults() {
        var m = new SettingsModel();
        m.LoadFrom(new Dictionary<string, string>());
        Assert.True(m.ShowMissionProgress);
        Assert.True(m.AutoExportCsv);
        Assert.True(m.AutoExportXlsx);
        Assert.Equal(1, m.WorkerCount);
    }

    [Fact]
    public void LoadFrom_ReadsAllKeys() {
        var settings = new Dictionary<string, string> {
            [SettingsModel.KeyAutoRefreshMenno] = "true",
            [SettingsModel.KeyWorkerCount] = "7",
            [SettingsModel.KeyScreenshotSafety] = "true",
            [SettingsModel.KeyShowMissionProgress] = "false",
            [SettingsModel.KeyAdvancedDropFilter] = "true",
            [SettingsModel.KeyAutoExportCsv] = "false",
            [SettingsModel.KeyAutoExportXlsx] = "false",
            [SettingsModel.KeyWorkerCountWarningRead] = "true",
            [SettingsModel.KeyWindowWidth] = "1600",
            [SettingsModel.KeyWindowHeight] = "900",
            [SettingsModel.KeyStartInFullscreen] = "true",
            [SettingsModel.KeyExportKeepCount] = "5",
            [SettingsModel.KeyStorageFolderHidden] = "true",
            [SettingsModel.KeyBackupDestPath] = @"C:\backups",
            [SettingsModel.KeyMoveDestPath] = @"D:\eggdata",
        };
        var m = new SettingsModel();
        m.LoadFrom(settings);

        Assert.True(m.AutoRefreshMenno);
        Assert.Equal(7, m.WorkerCount);
        Assert.True(m.ScreenshotSafety);
        Assert.False(m.ShowMissionProgress);
        Assert.True(m.AdvancedDropFilter);
        Assert.False(m.AutoExportCsv);
        Assert.False(m.AutoExportXlsx);
        Assert.True(m.WorkerCountWarningRead);
        Assert.Equal(1600, m.WindowWidth);
        Assert.Equal(900, m.WindowHeight);
        Assert.True(m.StartInFullscreen);
        Assert.Equal(5, m.ExportKeepCount);
        Assert.True(m.StorageFolderHidden);
        Assert.Equal(@"C:\backups", m.BackupDestPath);
        Assert.Equal(@"D:\eggdata", m.MoveDestPath);
    }

    [Fact]
    public void LoadFrom_ClampsOutOfRangeWorkerCount() {
        var m = new SettingsModel();
        m.LoadFrom(new Dictionary<string, string> { [SettingsModel.KeyWorkerCount] = "99" });
        Assert.Equal(10, m.WorkerCount);
    }

    [Fact]
    public void FormatBool_MatchesGo() {
        Assert.Equal("true", SettingsModel.FormatBool(true));
        Assert.Equal("false", SettingsModel.FormatBool(false));
    }
}
