using EggIdentity.UI;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace EggLedger.Web.Interop;

public sealed class OutsideClickRegistration(
    OutsideClickInterop interop, Func<Task> onOutsideClick, string? id = null, ILogger<OutsideClickRegistration>? logger = null) : IAsyncDisposable {

    private static long _nextId;

    private readonly string _id = id ?? $"outside-click-{Interlocked.Increment(ref _nextId)}";
    private DotNetObjectReference<OutsideClickRegistration>? _selfRef;
    private bool _registered;

    public async Task RegisterAsync(ElementReference element) {
        _selfRef ??= DotNetObjectReference.Create(this);
        try {
            await interop.RegisterAsync(_id, element, _selfRef);
            _registered = true;
        } catch (Exception ex) when (ex is JSDisconnectedException or ObjectDisposedException or TaskCanceledException) {
            logger?.LogDebug(ex, "outside click register failed for {Id}", _id);
        }
    }

    public async Task UnregisterAsync() {
        if (!_registered) {
            return;
        }

        _registered = false;
        try {
            await interop.UnregisterAsync(_id);
        } catch (Exception ex) when (ex is JSDisconnectedException or ObjectDisposedException or TaskCanceledException) {
            logger?.LogDebug(ex, "outside click unregister failed for {Id}", _id);
        }
    }

    [JSInvokable]
    public async Task OnOutsideClick() {
        _registered = false;
        await onOutsideClick();
    }

    public async ValueTask DisposeAsync() {
        if (_registered) {
            await UnregisterAsync();
        }

        _selfRef?.Dispose();
    }

}
