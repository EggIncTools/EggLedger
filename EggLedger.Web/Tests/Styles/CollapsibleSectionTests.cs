using Bunit;
using EggLedger.Web.Components;
using Microsoft.AspNetCore.Components;

namespace EggLedger.Web.Tests.Styles;

public class CollapsibleSectionTests : BunitContext {
    [Fact]
    public void Closed_hides_body_and_marks_collapsed() {
        var cut = Render<CollapsibleSection>(p => p
            .Add(x => x.Title, "Reports")
            .Add(x => x.ChildContent, (RenderFragment)(b => b.AddContent(0, "body"))));
        Assert.DoesNotContain("csec-open", cut.Find(".csec").ClassList);
        Assert.Equal("false", cut.Find("button").GetAttribute("aria-expanded"));
        Assert.Empty(cut.FindAll(".csec-body"));
    }

    [Fact]
    public void Open_renders_body_and_open_class() {
        var cut = Render<CollapsibleSection>(p => p
            .Add(x => x.Title, "Reports")
            .Add(x => x.Open, true)
            .Add(x => x.ChildContent, (RenderFragment)(b => b.AddContent(0, "body"))));
        Assert.Contains("csec-open", cut.Find(".csec").ClassList);
        Assert.Equal("body", cut.Find(".csec-body").TextContent);
    }

    [Fact]
    public void Head_click_raises_toggle() {
        var toggled = false;
        var cut = Render<CollapsibleSection>(p => p
            .Add(x => x.Title, "Reports")
            .Add(x => x.OnToggle, () => toggled = true));
        cut.Find("button").Click();
        Assert.True(toggled);
    }
}
