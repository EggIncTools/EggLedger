using EggLedger.CssBuild;

namespace EggLedger.Web.Tests.Styles;

public class GlobalSheetTests {
    private static readonly Lazy<string> Sheet = new(() => CssBuilder.Build(StylePaths.WebDir));

    [Theory]
    [InlineData(".modal-card")]
    [InlineData(".panel")]
    [InlineData(".btn-primary")]
    [InlineData(".tooltip-floating")]
    public void Emits_shared_component_layer(string selector) {
        Assert.Contains(selector, Sheet.Value);
    }

    [Fact]
    public void Emits_every_palette_token_unconditionally() {
        var missing = LedgerPalette.All
            .Where(t => !Sheet.Value.Contains($"--color-{t.Name}: {t.Value};"))
            .Select(t => t.Name)
            .ToList();
        Assert.Empty(missing);
    }

    [Theory]
    [InlineData("EggLedger.Desktop/wwwroot/desktop.html")]
    [InlineData("EggLedger.Web.Server/Components/AppHost.razor")]
    public void Host_pages_do_not_link_secondary_sheet(string hostPage) {
        var text = File.ReadAllText(Path.Combine(StylePaths.RepoRoot, hostPage));
        Assert.DoesNotContain("css/app.css", text);
    }

    [Fact]
    public void Emits_warn_token() {
        Assert.Contains("--color-warn: #fb923c;", Sheet.Value);
    }
}
