using EggLedger.Web.Pages;
using EggLedger.Web.State;

namespace EggLedger.Web.Tests.Pages;

public sealed class LedgerShellRouteTests {
    [Fact]
    public void BareMissionsAliasesToListAll() {
        var target = LedgerShell.RouteTarget("/missions");

        Assert.NotNull(target);
        Assert.Equal(LedgerView.Ships, target!.Value.View);
        Assert.Equal(ShipsViewMode.List, target.Value.Mode);
        Assert.Null(target.Value.Scope);
        Assert.False(target.Value.Settings);
        Assert.False(target.Value.About);
        Assert.Null(target.Value.Legal);
        Assert.False(target.Value.Support);
    }

    [Fact]
    public void CalendarHomeParsesModeAndScope() {
        var target = LedgerShell.RouteTarget("/missions/calendar/home");

        Assert.NotNull(target);
        Assert.Equal(LedgerView.Ships, target!.Value.View);
        Assert.Equal(ShipsViewMode.Calendar, target.Value.Mode);
        Assert.Equal(0, target.Value.Scope);
    }

    [Fact]
    public void ListVirtueSettingsParsesModeScopeAndModal() {
        var target = LedgerShell.RouteTarget("/missions/list/virtue/settings");

        Assert.NotNull(target);
        Assert.Equal(LedgerView.Ships, target!.Value.View);
        Assert.Equal(ShipsViewMode.List, target.Value.Mode);
        Assert.Equal(1, target.Value.Scope);
        Assert.True(target.Value.Settings);
    }

    [Fact]
    public void ModeOnlyWithModalFallsBackToDefaultScope() {
        var target = LedgerShell.RouteTarget("/missions/list/settings");

        Assert.NotNull(target);
        Assert.Equal(ShipsViewMode.List, target!.Value.Mode);
        Assert.Null(target.Value.Scope);
        Assert.True(target.Value.Settings);
    }

    [Fact]
    public void LifetimeTermsParsesLegalModalWithNoModeOrScope() {
        var target = LedgerShell.RouteTarget("/lifetime/terms");

        Assert.NotNull(target);
        Assert.Equal(LedgerView.Drops, target!.Value.View);
        Assert.Null(target.Value.Mode);
        Assert.Null(target.Value.Scope);
        Assert.Equal(LegalDocument.Terms, target.Value.Legal);
    }

    [Fact]
    public void ReportsSupportParsesSupportModal() {
        var target = LedgerShell.RouteTarget("/reports/support");

        Assert.NotNull(target);
        Assert.Equal(LedgerView.Reports, target!.Value.View);
        Assert.Null(target.Value.Mode);
        Assert.True(target.Value.Support);
    }

    [Fact]
    public void SettingsAliasMapsToShipsWithSettingsOpen() {
        var target = LedgerShell.RouteTarget("/settings");

        Assert.NotNull(target);
        Assert.Equal(LedgerView.Ships, target!.Value.View);
        Assert.Null(target.Value.Mode);
        Assert.True(target.Value.Settings);
        Assert.False(target.Value.About);
    }

    [Fact]
    public void AboutAliasMapsToShipsWithAboutOpen() {
        var target = LedgerShell.RouteTarget("/about");

        Assert.NotNull(target);
        Assert.Equal(LedgerView.Ships, target!.Value.View);
        Assert.Null(target.Value.Mode);
        Assert.True(target.Value.About);
        Assert.False(target.Value.Settings);
    }

    [Fact]
    public void UnknownModeAndScopeFallBackToDefaults() {
        var target = LedgerShell.RouteTarget("/missions/bogus/nonsense");

        Assert.NotNull(target);
        Assert.Equal(ShipsViewMode.List, target!.Value.Mode);
        Assert.Null(target.Value.Scope);
        Assert.False(target.Value.Settings);
        Assert.False(target.Value.About);
        Assert.Null(target.Value.Legal);
        Assert.False(target.Value.Support);
    }

    [Fact]
    public void UnknownModalSlugIsIgnoredRatherThanTreatedAsModal() {
        var target = LedgerShell.RouteTarget("/missions/list/all/bogus");

        Assert.NotNull(target);
        Assert.Equal(ShipsViewMode.List, target!.Value.Mode);
        Assert.Null(target.Value.Scope);
        Assert.False(target.Value.Settings);
        Assert.False(target.Value.About);
        Assert.Null(target.Value.Legal);
        Assert.False(target.Value.Support);
    }

    [Fact]
    public void UnknownRootReturnsNull() {
        Assert.Null(LedgerShell.RouteTarget("/nowhere"));
    }
}
