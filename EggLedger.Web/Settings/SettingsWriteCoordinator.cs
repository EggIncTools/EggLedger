using EggIdentity.UI;
using EggLedger.Web.Data;
using EggLedger.Web.Services;

namespace EggLedger.Web.Settings;

public sealed class SettingsWriteCoordinator(
    IndexedDbSettings settings,
    ScreenshotSafety screenshotSafety,
    CloudAutoSyncCoordinator autoSync) {
    public async Task SetAsync(string key, string value) {
        await settings.SetSettingAsync(key, value);

        if (key == SettingsModel.KeyScreenshotSafety) screenshotSafety.Enabled = value == "true";

        if (IsCloudSyncableKey(key)) await autoSync.NotifySettingsChangedAsync();
    }

    public static bool IsCloudSyncableKey(string key) {
        return key is
            SettingsModel.KeyAutoRefreshMenno or
            SettingsModel.KeyWorkerCount or
            SettingsModel.KeyScreenshotSafety or
            SettingsModel.KeyShowMissionProgress or
            SettingsModel.KeyAdvancedDropFilter;
    }
}
