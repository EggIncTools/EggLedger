using Microsoft.JSInterop;

namespace EggLedger.Web.Platform;

public sealed class BrowserPlatformCapabilities(IJSRuntime js) : IPlatformCapabilities, IAsyncDisposable {
    private const string ModulePath = "./_content/EggLedger.Web/js/platform.js";
    private readonly IJSRuntime _js = js;
    private IJSObjectReference? _module;

    public bool IsDesktop => false;
    public Task OpenFileAsync(string path) => Task.CompletedTask;
    public Task OpenFileInFolderAsync(string path) => Task.CompletedTask;

    public Task OpenUrlAsync(string url) => Task.CompletedTask;

    public Task<string?> ChooseSaveFilePathAsync(string defaultName) => Task.FromResult<string?>(null);

    public async Task RestartAppAsync() {
        try {
            _module ??= await _js.InvokeAsync<IJSObjectReference>("import", ModulePath);
            await _module.InvokeVoidAsync("reload");
        } catch (Exception ex) when (ex is JSDisconnectedException or ObjectDisposedException or TaskCanceledException) {
        }
    }

    public async Task<(int w, int h)> GetWindowSizeAsync() {
        try {
            _module ??= await _js.InvokeAsync<IJSObjectReference>("import", ModulePath);
            var dims = await _module.InvokeAsync<int[]>("windowSize");
            return (dims[0], dims[1]);
        } catch (Exception ex) when (ex is JSDisconnectedException or ObjectDisposedException or TaskCanceledException) {
            return (0, 0);
        }
    }

    public Task<string?> ChooseFolderAsync() => Task.FromResult<string?>(null);

    public Task SetFolderHiddenAsync(string path, bool hidden) => Task.CompletedTask;

    public string DataRootDir => "";

    public async ValueTask DisposeAsync() {
        if (_module is not null) {
            await _module.DisposeAsync();
        }
    }
}
