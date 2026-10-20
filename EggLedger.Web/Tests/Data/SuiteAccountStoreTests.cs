using EggIdentity.Contract;
using EggLedger.Domain.MissionQuery;
using EggLedger.Web.Data;

namespace EggLedger.Web.Tests.Data;

public sealed class SuiteAccountStoreTests {
    private const string Eid1 = "EI1111111111111111";
    private const string Eid2 = "EI2222222222222222";
    private const string Eid3 = "EI3333333333333333";

    private static AccountInfo Acct(string id, string nick = "Nick") =>
        new() { Id = id, Nickname = nick, EBString = "1.0q", AccountColor = "abcdef", SeString = "5.00B", PeCount = 3, TeCount = 1, LastBackupTime = 1700000000 };

    [Fact]
    public async Task EmptyByDefault() {
        var (store, _) = SuiteTestAccounts.Make(new IndexedDbSettings(new FakeIndexedDb()));
        Assert.Empty(await store.GetKnownAccountsAsync());
    }

    [Fact]
    public async Task AddRoundTripsEveryField() {
        var (store, _) = SuiteTestAccounts.Make(new IndexedDbSettings(new FakeIndexedDb()));
        await store.AddAccountAsync(Acct(Eid1, "Alice"));

        var got = Assert.Single(await store.GetKnownAccountsAsync());

        Assert.Equal(Eid1, got.Id);
        Assert.Equal("Alice", got.Nickname);
        Assert.Equal("1.0q", got.EBString);
        Assert.Equal("abcdef", got.AccountColor);
        Assert.Equal("5.00B", got.SeString);
        Assert.Equal(3, got.PeCount);
        Assert.Equal(1, got.TeCount);
        Assert.Equal(1700000000, got.LastBackupTime);
    }

    [Fact]
    public async Task AddRejectsMalformedEid() {
        var (store, _) = SuiteTestAccounts.Make(new IndexedDbSettings(new FakeIndexedDb()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AddAccountAsync(Acct("EI1")));
    }

    [Fact]
    public async Task AddPreservesInsertionOrder() {
        var (store, _) = SuiteTestAccounts.Make(new IndexedDbSettings(new FakeIndexedDb()));
        await store.AddAccountAsync(Acct(Eid1));
        await store.AddAccountAsync(Acct(Eid2));
        await store.AddAccountAsync(Acct(Eid3));

        Assert.Equal([Eid1, Eid2, Eid3], (await store.GetKnownAccountsAsync()).Select(a => a.Id));
    }

    [Fact]
    public async Task RefreshUpdatesExistingSnapshotInPlace() {
        var (store, _) = SuiteTestAccounts.Make(new IndexedDbSettings(new FakeIndexedDb()));
        await store.AddAccountAsync(Acct(Eid1, "Old"));
        await store.AddAccountAsync(Acct(Eid2));

        Assert.True(await store.RefreshAccountAsync(Acct(Eid1, "New")));

        var got = await store.GetKnownAccountsAsync();
        Assert.Equal([Eid1, Eid2], got.Select(a => a.Id));
        Assert.Equal("New", got[0].Nickname);
    }

    [Fact]
    public async Task RefreshNeverInsertsARemovedAccount() {
        var (store, _) = SuiteTestAccounts.Make(new IndexedDbSettings(new FakeIndexedDb()));
        await store.AddAccountAsync(Acct(Eid1));
        await store.RemoveAccountAsync(Eid1);

        Assert.False(await store.RefreshAccountAsync(Acct(Eid1)));
        Assert.Empty(await store.GetKnownAccountsAsync());
    }

    [Fact]
    public async Task LabelWinsOverGameName() {
        var (store, suite) = SuiteTestAccounts.Make(new IndexedDbSettings(new FakeIndexedDb()));
        await store.AddAccountAsync(Acct(Eid1, "GameName"));

        await suite.RenameEidAsync(Eid1, "Main");

        Assert.Equal("Main", Assert.Single(await store.GetKnownAccountsAsync()).Nickname);
    }

    [Fact]
    public async Task ImportsLegacyRowsAndActiveThenClearsThem() {
        var settings = new IndexedDbSettings(new FakeIndexedDb());
        await settings.SetSettingAsync(SuiteAccountStore.LegacyAccountsKey,
            $$"""[{"id":"{{Eid1}}","nickname":"Alice","account_color":"abcdef","pe_count":2},{"id":"{{Eid2}}","nickname":"Bob"},{"id":"bad","nickname":"X"}]""");
        await settings.SetSettingAsync(SuiteAccountStore.LegacyActiveKey, Eid2);
        var (store, suite) = SuiteTestAccounts.Make(settings);

        var got = await store.GetKnownAccountsAsync();

        Assert.Equal([Eid1, Eid2], got.Select(a => a.Id));
        Assert.Equal("Alice", got[0].Nickname);
        Assert.Equal(2, got[0].PeCount);
        Assert.Equal(Eid2, suite.ActiveEid);
        var all = await settings.GetAllSettingsAsync();
        Assert.False(all.ContainsKey(SuiteAccountStore.LegacyAccountsKey));
        Assert.False(all.ContainsKey(SuiteAccountStore.LegacyActiveKey));
    }

    [Fact]
    public async Task CorruptLegacyBlobIsDropped() {
        var settings = new IndexedDbSettings(new FakeIndexedDb());
        await settings.SetSettingAsync(SuiteAccountStore.LegacyAccountsKey, "not json{");
        var (store, _) = SuiteTestAccounts.Make(settings);

        Assert.Empty(await store.GetKnownAccountsAsync());
        Assert.False((await settings.GetAllSettingsAsync()).ContainsKey(SuiteAccountStore.LegacyAccountsKey));
    }

    [Fact]
    public async Task ImportAcceptsPartially() {
        var (store, _) = SuiteTestAccounts.Make(new IndexedDbSettings(new FakeIndexedDb()));

        var result = await store.ImportAsync([Acct(Eid1), Acct("nope")]);

        Assert.NotNull(result);
        Assert.Equal(EidRules.FormatReason, Assert.Single(result.Rejected).Reason);
        Assert.Equal(Eid1, Assert.Single(await store.GetKnownAccountsAsync()).Id);
    }
}
