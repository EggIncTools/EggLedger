using EggLedger.Domain.MissionQuery;
using EggLedger.Web.Services;
using EggLedger.Web.State;

namespace EggLedger.Web.Tests.State;

public sealed class AppStateServiceTests {
    [Fact]
    public void DefaultsToNoVersionAndNoAccounts() {
        var sut = new AppStateService();
        Assert.Equal("", sut.AppVersion);
        Assert.Empty(sut.KnownAccounts);
        Assert.Null(sut.PipelineState);
    }

    [Fact]
    public void SettingAppVersionFiresChanged() {
        var sut = new AppStateService();
        var fired = 0;
        sut.Changed += () => fired++;

        sut.AppVersion = "2.1.4";

        Assert.Equal("2.1.4", sut.AppVersion);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void SettingSameValueDoesNotFireChanged() {
        var sut = new AppStateService { AppVersion = "2.1.4" };
        var fired = 0;
        sut.Changed += () => fired++;

        sut.AppVersion = "2.1.4";

        Assert.Equal(0, fired);
    }

    [Fact]
    public void SettingKnownAccountsUpdatesAndFires() {
        var sut = new AppStateService();
        var fired = 0;
        sut.Changed += () => fired++;

        var accounts = new List<KnownAccount>
        {
            new() { Id = "EI1", Nickname = "Alice" },
        };
        sut.KnownAccounts = accounts;

        Assert.Single(sut.KnownAccounts);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void PipelineStateReusesFetchAppStateEnum() {
        var sut = new AppStateService();
        var fired = 0;
        sut.Changed += () => fired++;

        sut.PipelineState = AppState.FetchingMissions;

        Assert.Equal(AppState.FetchingMissions, sut.PipelineState);
        Assert.Equal(1, fired);
    }
}
