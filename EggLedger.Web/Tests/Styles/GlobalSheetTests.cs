using EggIdentity.Styles;
using EggIdentity.Styles.Css;
using EggIdentity.Styles.Theming;

namespace EggLedger.Web.Tests.Styles;

public class GlobalSheetTests {
    private static IReadOnlySet<string> Palette =>
        StylePaths.AppSheet.Rule(":root")?.Declarations.Select(d => d.Property).Where(p => p.StartsWith("--color-", StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal)
        ?? throw new InvalidOperationException("app.css has no :root rule.");

    [Fact]
    public void Root_declares_every_required_token() {
        Assert.DoesNotContain(ComponentTokens.Required, t => !Palette.Contains(t));
    }

    [Fact]
    public void Every_color_token_read_anywhere_is_declared_in_root() {
        var sheets = new[] { StylePaths.SharedSheet, StylePaths.AppSheet }
            .Concat(StylePaths.ScopedCssFiles().Select(CssSheet.Load));
        var undeclared = sheets
            .SelectMany(s => s.ReadProperties)
            .Where(p => p.StartsWith("--color-", StringComparison.Ordinal) && !Palette.Contains(p))
            .Distinct()
            .ToList();
        Assert.Empty(undeclared);
    }

    [Fact]
    public void Palette_tokens_round_trip_theme_registry() {
        var registry = new ThemeTokenRegistry();
        var names = Palette.Select(n => n["--color-".Length..]).ToList();
        foreach (var name in names.Where(n => !ThemeTokenRegistry.BaselineSettable.Contains(n))) {
            registry.Register(name);
        }
        Assert.DoesNotContain(names, name => !registry.IsKnown(name) || registry.Canonicalize(name) != name);
    }

    [Fact]
    public void App_sheet_has_no_engine_or_layer_syntax() {
        var sheet = StylePaths.AppSheet;
        Assert.Empty(sheet.Statements);
        Assert.DoesNotContain(sheet.Containers, c => !c.StartsWith("@media", StringComparison.Ordinal));
        Assert.DoesNotContain(sheet.Selectors, s => s.StartsWith('@'));
    }

    [Theory]
    [InlineData("EggLedger.Desktop/wwwroot/desktop.html")]
    [InlineData("EggLedger.Web.Server/Components/AppHost.razor")]
    public void Host_pages_link_shared_then_app_then_scoped(string hostPage) {
        var text = File.ReadAllText(Path.Combine(StylePaths.RepoRoot, hostPage));
        var shared = text.IndexOf("_content/EggIdentity.UI/shared.css", StringComparison.Ordinal);
        var app = text.IndexOf("_content/EggLedger.Web/app.css", StringComparison.Ordinal);
        var scoped = text.IndexOf(".styles.css", StringComparison.Ordinal);
        Assert.True(shared >= 0 && shared < app && app < scoped, $"{hostPage}: link order must be shared.css, app.css, scoped bundle.");
    }
}
