using EggIdentity.UI.Suite;
using EggLedger.Domain.MissionQuery;
using EggLedger.Web.Data;
using EggLedger.Web.State;
using EggLedger.Web.Tests.Data;

namespace EggLedger.Web.Tests.State;

public sealed class AccountLoaderTests {
    private const string Eid1 = "EI1111111111111111";
    private const string Eid2 = "EI2222222222222222";

    private static (AccountLoader Loader, SuiteAccountStore Store, SuiteContext Suite, AppStateService App, ActiveAccount Active) Make() {
        var (store, suite) = SuiteTestAccounts.Make(new IndexedDbSettings(new FakeIndexedDb()));
        var app = new AppStateService();
        var active = new ActiveAccount();
        return (new AccountLoader(store, suite, app, active), store, suite, app, active);
    }

    private static AccountInfo Acct(string id, string nick = "Nick") =>
        new() { Id = id, Nickname = nick };

    [Fact]
    public async Task EnsureLoadedPopulatesAppStateKnownAccounts() {
        var (loader, store, _, app, _) = Make();
        await store.AddAccountAsync(Acct(Eid1, "Alice"));

        await loader.EnsureLoadedAsync();

        Assert.Single(app.KnownAccounts);
        Assert.Equal("Alice", app.KnownAccounts[0].Nickname);
        Assert.Single(loader.Accounts);
    }

    [Fact]
    public async Task EnsureLoadedRestoresSuiteActiveAccount() {
        var (loader, store, suite, _, active) = Make();
        await store.AddAccountAsync(Acct(Eid1));
        await suite.SetActiveEidAsync(Eid1);

        await loader.EnsureLoadedAsync();

        Assert.Equal(Eid1, active.ActiveAccountId);
    }

    [Fact]
    public async Task ActiveChangeWritesToSuite() {
        var (loader, store, suite, _, active) = Make();
        await store.AddAccountAsync(Acct(Eid1));
        await loader.EnsureLoadedAsync();

        active.SetActive(Eid1);

        Assert.Equal(Eid1, suite.ActiveEid);
        loader.Dispose();
    }

    [Fact]
    public async Task SuiteRemoveClearsActiveAndList() {
        var (loader, store, suite, app, active) = Make();
        await store.AddAccountAsync(Acct(Eid1));
        await suite.SetActiveEidAsync(Eid1);
        await loader.EnsureLoadedAsync();

        await suite.RemoveEidAsync(Eid1);

        Assert.Empty(app.KnownAccounts);
        Assert.Null(active.ActiveAccountId);
        loader.Dispose();
    }

    [Fact]
    public async Task SuiteAddShowsUpWithoutRefresh() {
        var (loader, store, _, app, _) = Make();
        await loader.EnsureLoadedAsync();
        Assert.Empty(app.KnownAccounts);

        await store.AddAccountAsync(Acct(Eid2, "Bob"));

        Assert.Equal("Bob", Assert.Single(app.KnownAccounts).Nickname);
        loader.Dispose();
    }
}
