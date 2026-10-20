using EggIdentity.Auth;
using EggIdentity.UI;
using EggIdentity.UI.Suite;
using EggLedger.Web.Data;

namespace EggLedger.Web.Tests.Data;

internal static class SuiteTestAccounts {
    public static (SuiteAccountStore Accounts, SuiteContext Suite) Make(IndexedDbSettings settings) {
        var cookies = new ViewerCookieOptions();
        foreach (var name in ViewerCookieOptions.SuiteCookies) cookies.Allow(name, TimeSpan.FromDays(1));
        var suite = new SuiteContext(
            new SuiteOptions { CurrentSlug = "ledger" },
            new AnonymousUser(),
            new ViewerCookies(cookies),
            eidStore: new SettingsEidStore(settings));
        return (new SuiteAccountStore(suite, settings), suite);
    }

    private sealed class AnonymousUser : ICurrentUser {
        public CurrentUser Current => CurrentUser.Anonymous;

        public Task<CurrentUser> GetAsync() => Task.FromResult(CurrentUser.Anonymous);
    }
}
