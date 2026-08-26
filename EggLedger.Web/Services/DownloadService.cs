using EggLedger.Domain.Export;
using Microsoft.JSInterop;

namespace EggLedger.Web.Services;

public sealed class DownloadService(IJSRuntime js) : IDownloadService, IAsyncDisposable {
    private const string ModulePath = "./_content/EggLedger.Web/js/download.js";
    private const string CsvMime = "text/csv";
    private const string XlsxMime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private IJSObjectReference? _module;

    private async ValueTask<IJSObjectReference> ModuleAsync()
        => _module ??= await js.InvokeAsync<IJSObjectReference>("import", ModulePath);

    public async ValueTask DownloadCsvAsync(IReadOnlyList<Mission> missions, string filename)
        => await DownloadAsync(MissionExport.MissionsToCsvBytes(missions), filename, CsvMime);

    public async ValueTask DownloadXlsxAsync(IReadOnlyList<Mission> missions, string filename)
        => await DownloadAsync(MissionExport.MissionsToXlsxBytes(missions), filename, XlsxMime);

    public async ValueTask DownloadJsonAsync(string json, string filename) {
        var module = await ModuleAsync();
        await module.InvokeVoidAsync("downloadText", filename, json, "application/json");
    }

    public async ValueTask<string?> PickJsonFileAsync() {
        var module = await ModuleAsync();
        return await module.InvokeAsync<string?>("pickTextFile", ".json,application/json");
    }

    private async ValueTask DownloadAsync(byte[] bytes, string filename, string mime) {
        var module = await ModuleAsync();
        await module.InvokeVoidAsync("download", filename, Convert.ToBase64String(bytes), mime);
    }

    public async ValueTask DisposeAsync() {
        if (_module is not null) {
            await _module.DisposeAsync();
            _module = null;
        }
    }
}
