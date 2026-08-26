using EggLedger.Domain.MissionQuery;
using EggLedger.Web.Data;

namespace EggLedger.Web.State;

public sealed class AccountLoader(
    IndexedDbAccountStore store, AppStateService appState, ActiveAccount active) : IDisposable {

    private bool _loaded;
    private bool _subscribed;
    private bool _persisting;

    public IReadOnlyList<AccountInfo> Accounts { get; private set; } = [];

    public async Task EnsureLoadedAsync() {
        if (!_subscribed) {
            active.Changed += OnActiveChanged;
            _subscribed = true;
        }

        await RefreshAsync().ConfigureAwait(false);

        if (!_loaded) {
            var activeId = await store.GetActiveAccountIdAsync().ConfigureAwait(false);
            if (!string.IsNullOrEmpty(activeId)) {
                _persisting = true;
                active.SetActive(activeId);
                _persisting = false;
            }
            _loaded = true;
        }
    }

    public async Task RefreshAsync() {
        Accounts = await store.GetKnownAccountsAsync().ConfigureAwait(false);
        appState.KnownAccounts = Accounts.Select(a => a.ToKnownAccount()).ToList();
    }

    private void OnActiveChanged() {
        if (_persisting) {
            return;
        }

        _ = store.SetActiveAccountIdAsync(active.ActiveAccountId ?? "");
    }

    public void Dispose() {
        if (_subscribed) {
            active.Changed -= OnActiveChanged;
            _subscribed = false;
        }
    }
}
