using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace EggLedger.Web.Interop;

public sealed class JsResizeObserver(IJSRuntime js) : IAsyncDisposable {
    private const string ModulePath = "./_content/EggLedger.Web/js/resizeObserver.js";
    private IJSObjectReference? _module;
    private IJSObjectReference? _handle;

    public async Task ObserveAsync<T>(ElementReference element, DotNetObjectReference<T> dotNetRef, string methodName) where T : class {
        try {
            _module = await js.InvokeAsync<IJSObjectReference>("import", ModulePath);
            _handle = await _module.InvokeAsync<IJSObjectReference>("observe", element, dotNetRef, methodName);
        } catch (Exception ex) when (ex is JSDisconnectedException or ObjectDisposedException or TaskCanceledException) {
        }
    }

    public async ValueTask DisposeAsync() {
        try {
            if (_handle is not null && _module is not null) {
                await _module.InvokeVoidAsync("unobserve", _handle);
                await _handle.DisposeAsync();
            }

            if (_module is not null) {
                await _module.DisposeAsync();
            }
        } catch (Exception ex) when (ex is JSDisconnectedException or ObjectDisposedException or TaskCanceledException) {
        }
    }
}
