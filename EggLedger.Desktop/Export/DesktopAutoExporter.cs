using System.Globalization;
using EggLedger.Desktop.Storage;
using EggLedger.Domain.Export;
using EggLedger.Domain.MissionQuery;
using EggLedger.Web.Data;
using EggLedger.Web.Services;
using EggLedger.Web.Settings;

namespace EggLedger.Desktop.Export;

public sealed class DesktopAutoExporter : AutoExporterBase {
    private readonly string _exportsDir;

    public DesktopAutoExporter(
        string dataRootDir, IndexedDbSettings settings, IMissionStore store, MissionQueryHandlers queries)
        : base(settings, store, queries) {
        _exportsDir = StoragePaths.ResolveExportsDir(dataRootDir);
    }

    protected override async Task DeliverAsync(
        string accountId,
        string nickname,
        IReadOnlyList<Mission> missions,
        SettingsModel model,
        CancellationToken cancellationToken) {
        var missionsDir = Path.Combine(_exportsDir, "missions");
        Directory.CreateDirectory(missionsDir);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);

        if (model.AutoExportCsv) {
            var path = Path.Combine(missionsDir, $"{accountId}.{stamp}.csv");
            await File.WriteAllBytesAsync(path, MissionExport.MissionsToCsvBytes(missions), cancellationToken).ConfigureAwait(false);
        }

        if (model.AutoExportXlsx) {
            var path = Path.Combine(missionsDir, $"{accountId}.{stamp}.xlsx");
            await File.WriteAllBytesAsync(path, MissionExport.MissionsToXlsxBytes(missions), cancellationToken).ConfigureAwait(false);
        }

        if (model.ExportKeepCount > 0) {
            ExportManagement.PruneForPlayer(_exportsDir, accountId, model.ExportKeepCount);
        }
    }
}
