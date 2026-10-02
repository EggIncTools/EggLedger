using System.Text.RegularExpressions;
using EggIdentity.Styles;
using EggIdentity.Styles.Theming;

namespace EggLedger.Web.Tests.Styles;

public partial class GlobalSheetTests {
    private static readonly Lazy<string> App = new(() => StylePaths.AppSheet);

    [Theory]
    [InlineData(".modal-card")]
    [InlineData(".panel")]
    [InlineData(".btn-primary")]
    [InlineData(".tooltip-floating")]
    public void Shared_sheet_ships_component_layer(string selector) {
        Assert.Contains(selector, StylePaths.SharedSheet);
    }

    [Fact]
    public void App_sheet_declares_every_required_token() {
        var declared = PaletteNames();
        var missing = ComponentTokens.Required.Where(t => !declared.Contains(t)).ToList();
        Assert.Empty(missing);
    }

    [Fact]
    public void Palette_tokens_round_trip_theme_registry() {
        var registry = new ThemeTokenRegistry();
        var names = PaletteNames().Select(n => n["--color-".Length..]).ToList();
        foreach (var name in names.Where(n => !ThemeTokenRegistry.BaselineSettable.Contains(n))) {
            registry.Register(name);
        }
        var failed = names.Where(name => !registry.IsKnown(name) || registry.Canonicalize(name) != name).ToList();
        Assert.Empty(failed);
    }

    [Fact]
    public void App_sheet_has_no_engine_syntax() {
        Assert.DoesNotContain("@theme", App.Value);
        Assert.DoesNotContain("@layer", App.Value);
        Assert.DoesNotContain("@apply", App.Value);
    }

    [Theory]
    [InlineData("EggLedger.Desktop/wwwroot/desktop.html")]
    [InlineData("EggLedger.Web.Server/Components/AppHost.razor")]
    public void Host_pages_link_shared_then_app_then_scoped(string hostPage) {
        var text = File.ReadAllText(Path.Combine(StylePaths.RepoRoot, hostPage));
        var shared = text.IndexOf("_content/EggIdentity.Styles/shared.css", StringComparison.Ordinal);
        var app = text.IndexOf("_content/EggLedger.Web/app.css", StringComparison.Ordinal);
        var scoped = text.IndexOf(".styles.css", StringComparison.Ordinal);
        Assert.True(shared >= 0 && shared < app && app < scoped, $"{hostPage}: link order must be shared.css, app.css, scoped bundle.");
        Assert.DoesNotContain("_content/EggLedger.Web/styles.css", text);
        Assert.DoesNotContain("css/app.css", text);
    }

    private static HashSet<string> PaletteNames() =>
        ColorDecl().Matches(App.Value).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

    [GeneratedRegex(@"(--color-[\w-]+)\s*:")]
    private static partial Regex ColorDecl();
}
