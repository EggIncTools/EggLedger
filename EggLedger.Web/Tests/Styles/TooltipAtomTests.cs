using EggLedger.CssBuild;

namespace EggLedger.Web.Tests.Styles;

public class TooltipAtomTests {
    private static readonly Lazy<string> Sheet = new(() => CssBuilder.Build(StylePaths.WebDir));

    private static string Script =>
        File.ReadAllText(Path.Combine(StylePaths.WebDir, "wwwroot", "js", "reportChartTooltip.js"));

    [Theory]
    [InlineData(".tooltip-line")]
    [InlineData(".tooltip-line-head")]
    public void Shared_layer_emits_tooltip_line_classes(string selector) {
        Assert.Contains(selector, Sheet.Value);
    }

    [Fact]
    public void Interim_tooltip_atoms_are_gone() {
        Assert.DoesNotContain("ledger-tt-line", Sheet.Value);
        Assert.DoesNotContain("ledger-tt-line", Script);
    }

    [Theory]
    [InlineData("text-gray-200")]
    [InlineData("text-gray-400")]
    [InlineData("font-medium")]
    public void Tooltip_script_emits_no_utilities(string utility) {
        Assert.DoesNotContain(utility, Script);
    }

    [Fact]
    public void Tooltip_script_emits_shared_line_classes() {
        Assert.Contains("tooltip-line-head", Script);
    }
}
