using System.Globalization;
using EggLedger.Desktop.Storage;
using EggLedger.Domain.Export;
using EggLedger.Domain.MissionQuery;
using EggLedger.Web.Data;
using EggLedger.Web.Services;
using EggLedger.Web.Settings;

namespace EggLedger.Desktop.Export;

public sealed class DesktopAutoExporter(
    string dataRootDir, IndexedDbSettings settings, IMissionStore store, MissionQueryHandlers queries, TimeProvider? time = null)
    : AutoExporterBase(settings, store, queries) {

    private readonly string _exportsDir = StoragePaths.ResolveExportsDir(dataRootDir);
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    protected override async Task<IReadOnlyList<string>> DeliverAsync(
        string accountId,
        string nickname,
        IReadOnlyList<Mission> missions,
        SettingsModel model,
        CancellationToken cancellationToken) {
        var missionsDir = Path.Combine(_exportsDir, "missions");
        Directory.CreateDirectory(missionsDir);
        var stamp = _time.GetLocalNow().ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        List<string> delivered = [];

        if (model.AutoExportCsv) {
            var path = Path.Combine(missionsDir, $"{accountId}.{stamp}.csv");
            await File.WriteAllBytesAsync(path, MissionExport.MissionsToCsvBytes(missions), cancellationToken).ConfigureAwait(false);
            delivered.Add(Path.GetRelativePath(dataRootDir, path));
        }

        if (model.AutoExportXlsx) {
            var path = Path.Combine(missionsDir, $"{accountId}.{stamp}.xlsx");
            await File.WriteAllBytesAsync(path, MissionExport.MissionsToXlsxBytes(missions), cancellationToken).ConfigureAwait(false);
            delivered.Add(Path.GetRelativePath(dataRootDir, path));
        }

        if (model.ExportKeepCount > 0) {
            ExportManagement.PruneForPlayer(_exportsDir, accountId, model.ExportKeepCount);
        }
        return delivered;
    }
}
