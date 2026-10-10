using System.Text;
using EggLedger.Domain.Export;
using EggLedger.Web.Platform;
using EggLedger.Web.Services;

namespace EggLedger.Desktop.Export;

public sealed class DesktopDownloadService(IPlatformCapabilities platform) : IDownloadService {
    public ValueTask DownloadCsvAsync(IReadOnlyList<Mission> missions, string filename)
        => SaveAsync(MissionExport.MissionsToCsvBytes(missions), filename);

    public ValueTask DownloadXlsxAsync(IReadOnlyList<Mission> missions, string filename)
        => SaveAsync(MissionExport.MissionsToXlsxBytes(missions), filename);

    public ValueTask DownloadJsonAsync(string json, string filename)
        => SaveAsync(Encoding.UTF8.GetBytes(json), filename);

    private async ValueTask SaveAsync(byte[] bytes, string filename) {
        var path = await platform.ChooseSaveFilePathAsync(filename).ConfigureAwait(false);
        if (string.IsNullOrEmpty(path)) {
            return;
        }
        await File.WriteAllBytesAsync(path, bytes).ConfigureAwait(false);
        await platform.OpenFileInFolderAsync(path).ConfigureAwait(false);
    }
}
