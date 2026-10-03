using EggIdentity.UI;
using EggLedger.Domain.Export;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace EggLedger.Web.Services;

public sealed class DownloadService(IJSRuntime js, ILogger<DownloadService> logger) : IDownloadService, IAsyncDisposable {
    private const string ModulePath = "./_content/EggLedger.Web/js/download.js";
    private const string CsvMime = "text/csv";
    private const string XlsxMime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private readonly DownloadInterop _download = new(js);
    private IJSObjectReference? _module;

    public async ValueTask DownloadCsvAsync(IReadOnlyList<Mission> missions, string filename)
        => await _download.SaveBytesAsync(filename, MissionExport.MissionsToCsvBytes(missions), CsvMime);

    public async ValueTask DownloadXlsxAsync(IReadOnlyList<Mission> missions, string filename)
        => await _download.SaveBytesAsync(filename, MissionExport.MissionsToXlsxBytes(missions), XlsxMime);

    public async ValueTask DownloadJsonAsync(string json, string filename)
        => await _download.SaveTextAsync(filename, json, "application/json");

    public async ValueTask<string?> PickJsonFileAsync() {
        try {
            _module ??= await js.InvokeAsync<IJSObjectReference>("import", ModulePath);
            return await _module.InvokeAsync<string?>("pickTextFile", ".json,application/json");
        } catch (Exception ex) when (ex is JSDisconnectedException or ObjectDisposedException or TaskCanceledException) {
            logger.LogDebug(ex, "json file pick failed for {Module}", ModulePath);
            return null;
        }
    }

    public async ValueTask DisposeAsync() {
        if (_module is not null) {
            await _module.DisposeAsync();
            _module = null;
        }
    }
}
