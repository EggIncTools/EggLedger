using System.Text;
using EggLedger.Domain.Export;
using EggLedger.Web.Platform;
using EggLedger.Web.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

namespace EggLedger.Desktop.Export;

public sealed class DesktopDownloadService(
    IPlatformCapabilities platform, IJSRuntime js, ILogger<DesktopDownloadService>? logger = null) : IDownloadService {
    private const string ModulePath = "./_content/EggLedger.Web/js/download.js";
    private readonly IPlatformCapabilities _platform = platform;
    private readonly IJSRuntime _js = js;
    private readonly ILogger<DesktopDownloadService> _logger = logger ?? NullLogger<DesktopDownloadService>.Instance;

    public ValueTask DownloadCsvAsync(IReadOnlyList<Mission> missions, string filename)
        => SaveAsync(MissionExport.MissionsToCsvBytes(missions), filename);

    public ValueTask DownloadXlsxAsync(IReadOnlyList<Mission> missions, string filename)
        => SaveAsync(MissionExport.MissionsToXlsxBytes(missions), filename);

    public ValueTask DownloadJsonAsync(string json, string filename)
        => SaveAsync(Encoding.UTF8.GetBytes(json), filename);

    public async ValueTask<string?> PickJsonFileAsync() {
        try {
            var module = await _js.InvokeAsync<IJSObjectReference>("import", ModulePath).ConfigureAwait(false);
            await using (module.ConfigureAwait(false)) {
                return await module.InvokeAsync<string?>("pickTextFile", ".json,application/json").ConfigureAwait(false);
            }
        } catch (Exception ex) when (ex is JSDisconnectedException or ObjectDisposedException or TaskCanceledException) {
            _logger.LogDebug(ex, "export: json file picker unavailable");
            return null;
        }
    }

    private async ValueTask SaveAsync(byte[] bytes, string filename) {
        var path = await _platform.ChooseSaveFilePathAsync(filename).ConfigureAwait(false);
        if (string.IsNullOrEmpty(path)) {

            return;
        }
        await File.WriteAllBytesAsync(path, bytes).ConfigureAwait(false);
        await _platform.OpenFileInFolderAsync(path).ConfigureAwait(false);
    }
}
