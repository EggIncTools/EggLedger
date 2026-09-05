using EggIdentity.Resilience;
using EggIdentity.UI;

namespace EggLedger.Web.Services;

public sealed class CloudAutoSyncCoordinator(ToastService toast, CircuitBreaker breaker) {
    public event Func<Task>? Triggered;

    public async Task NotifySettingsChangedAsync() {
        if (Triggered is null) {
            return;
        }

        if (breaker.State == CircuitState.Open) {
            PushUnreachableToast();
            return;
        }

        try {
            await Triggered.Invoke();
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            PushUnreachableToast();
        }
    }

    private void PushUnreachableToast() {
        toast.Push(
            StatusNoteKind.Busy,
            "sync unreachable, working offline",
            "Retry",
            () => _ = NotifySettingsChangedAsync());
    }
}
