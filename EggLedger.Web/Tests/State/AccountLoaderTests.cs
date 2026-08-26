using EggLedger.Domain.MissionQuery;
using EggLedger.Web.Data;
using EggLedger.Web.State;
using EggLedger.Web.Tests.Data;

namespace EggLedger.Web.Tests.State;

public sealed class AccountLoaderTests {
    private static (AccountLoader loader, IndexedDbAccountStore store, AppStateService app, ActiveAccount active) Make() {
        var db = new FakeIndexedDb();
        var store = new IndexedDbAccountStore(new IndexedDbSettings(db));
        var app = new AppStateService();
        var active = new ActiveAccount();
        return (new AccountLoader(store, app, active), store, app, active);
    }

    private static AccountInfo Acct(string id, string nick = "Nick") =>
        new() { Id = id, Nickname = nick };

    [Fact]
    public async Task EnsureLoadedPopulatesAppStateKnownAccounts() {
        var (loader, store, app, _) = Make();
        await store.AddKnownAccountAsync(Acct("EI1", "Alice"));

        await loader.EnsureLoadedAsync();

        Assert.Single(app.KnownAccounts);
        Assert.Equal("Alice", app.KnownAccounts[0].Nickname);
        Assert.Single(loader.Accounts);
    }

    [Fact]
    public async Task EnsureLoadedRestoresPersistedActiveAccount() {
        var (loader, store, _, active) = Make();
        await store.AddKnownAccountAsync(Acct("EI1"));
        await store.SetActiveAccountIdAsync("EI1");

        await loader.EnsureLoadedAsync();

        Assert.Equal("EI1", active.ActiveAccountId);
    }

    [Fact]
    public async Task ActiveChangePersistsToStore() {
        var (loader, store, _, active) = Make();
        await loader.EnsureLoadedAsync();

        active.SetActive("EI7");


        Assert.Equal("EI7", await store.GetActiveAccountIdAsync());
        loader.Dispose();
    }

    [Fact]
    public async Task RefreshPicksUpNewlyAddedAccount() {
        var (loader, store, app, _) = Make();
        await loader.EnsureLoadedAsync();
        Assert.Empty(app.KnownAccounts);

        await store.AddKnownAccountAsync(Acct("EI2", "Bob"));
        await loader.RefreshAsync();

        Assert.Single(app.KnownAccounts);
        Assert.Equal("Bob", app.KnownAccounts[0].Nickname);
    }
}
