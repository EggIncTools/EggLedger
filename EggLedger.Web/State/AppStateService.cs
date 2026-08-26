using EggLedger.Domain.MissionQuery;
using EggLedger.Web.Services;

namespace EggLedger.Web.State;

public sealed class AppStateService {
    public string AppVersion {
        get;
        set => Set(ref field, value);
    } = "";

    public IReadOnlyList<KnownAccount> KnownAccounts {
        get;
        set => Set(ref field, value);
    } = [];

    public AppState? PipelineState {
        get;
        set => Set(ref field, value);
    }

    public event Action? Changed;

    private void Set<T>(ref T field, T value) {
        if (EqualityComparer<T>.Default.Equals(field, value)) {
            return;
        }

        field = value;
        Changed?.Invoke();
    }
}
