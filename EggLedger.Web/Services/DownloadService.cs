using EggLedger.Domain.Export;
using Microsoft.JSInterop;

namespace EggLedger.Web.Services;

public sealed class DownloadService(IJSRuntime js) : IDownloadService, IAsyncDisposable {
    private const string ModulePath = "./_content/EggLedger.Web/js/download.js";
    private const string CsvMime = "text/csv";
    private const string XlsxMime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private IJSObjectReference? _module;

    public async ValueTask DownloadCsvAsync(IReadOnlyList<Mission> missions, string filename)
        => await DownloadAsync(MissionExport.MissionsToCsvBytes(missions), filename, CsvMime);

    public async ValueTask DownloadXlsxAsync(IReadOnlyList<Mission> missions, string filename)
        => await DownloadAsync(MissionExport.MissionsToXlsxBytes(missions), filename, XlsxMime);

    public async ValueTask DownloadJsonAsync(string json, string filename) {
        try {
            _module ??= await js.InvokeAsync<IJSObjectReference>("import", ModulePath);
            await _module.InvokeVoidAsync("downloadText", filename, json, "application/json");
        } catch (Exception ex) when (ex is JSDisconnectedException or ObjectDisposedException or TaskCanceledException) {
        }
    }

    public async ValueTask<string?> PickJsonFileAsync() {
        try {
            _module ??= await js.InvokeAsync<IJSObjectReference>("import", ModulePath);
            return await _module.InvokeAsync<string?>("pickTextFile", ".json,application/json");
        } catch (Exception ex) when (ex is JSDisconnectedException or ObjectDisposedException or TaskCanceledException) {
            return null;
        }
    }

    private async ValueTask DownloadAsync(byte[] bytes, string filename, string mime) {
        try {
            _module ??= await js.InvokeAsync<IJSObjectReference>("import", ModulePath);
            await _module.InvokeVoidAsync("download", filename, Convert.ToBase64String(bytes), mime);
        } catch (Exception ex) when (ex is JSDisconnectedException or ObjectDisposedException or TaskCanceledException) {
        }
    }

    public async ValueTask DisposeAsync() {
        if (_module is not null) {
            await _module.DisposeAsync();
            _module = null;
        }
    }
}
