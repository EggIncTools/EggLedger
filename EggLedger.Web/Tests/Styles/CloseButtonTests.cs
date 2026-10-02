using Bunit;
using EggLedger.Web.Components;

namespace EggLedger.Web.Tests.Styles;

public class CloseButtonTests : BunitContext {
    [Fact]
    public void Defaults_to_corner_variant() {
        var cut = Render<CloseButton>();
        var button = cut.Find("button");
        Assert.Contains("close-button", button.ClassList);
        Assert.Contains("close-button-corner", button.ClassList);
        Assert.Contains("close-button-icon", cut.Find("svg").ClassList);
    }

    [Fact]
    public void Inline_variant_sets_inline_class() {
        var cut = Render<CloseButton>(p => p.Add(x => x.Variant, CloseButton.CloseButtonVariant.Inline));
        Assert.Contains("close-button-inline", cut.Find("button").ClassList);
    }
}
