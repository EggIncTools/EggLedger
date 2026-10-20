using EggIdentity.UI.Suite;
using EggLedger.Domain.MissionQuery;
using EggLedger.Web.Data;

namespace EggLedger.Web.State;

public sealed class AccountLoader(
    SuiteAccountStore store, SuiteContext suite, AppStateService appState, ActiveAccount active) : IDisposable {

    private bool _loaded;
    private bool _subscribed;
    private bool _syncing;

    public IReadOnlyList<AccountInfo> Accounts { get; private set; } = [];

    public async Task EnsureLoadedAsync() {
        if (!_subscribed) {
            active.Changed += OnActiveChanged;
            suite.Changed += OnSuiteChanged;
            _subscribed = true;
        }

        await RefreshAsync().ConfigureAwait(false);

        if (!_loaded) {
            _loaded = true;
            SyncActiveFromSuite();
        }
    }

    public async Task RefreshAsync() {
        await store.EnsureReadyAsync().ConfigureAwait(false);
        Publish();
    }

    private void Publish() {
        Accounts = store.Current;
        appState.KnownAccounts = [.. Accounts.Select(a => a.ToKnownAccount())];
    }

    private void OnSuiteChanged() {
        Publish();
        if (_loaded) SyncActiveFromSuite();
    }

    private void SyncActiveFromSuite() {
        var id = suite.ActiveEid;
        _syncing = true;
        active.SetActive(id is not null && Accounts.Any(a => a.Id == id) ? id : null);
        _syncing = false;
    }

    private void OnActiveChanged() {
        if (_syncing) return;

        var id = active.ActiveAccountId;
        if (id == suite.ActiveEid) return;
        _ = suite.SetActiveEidAsync(id, Accounts.FirstOrDefault(a => a.Id == id)?.Nickname);
    }

    public void Dispose() {
        if (_subscribed) {
            active.Changed -= OnActiveChanged;
            suite.Changed -= OnSuiteChanged;
            _subscribed = false;
        }
    }
}
