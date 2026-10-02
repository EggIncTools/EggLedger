using Bunit;
using EggLedger.Web.Components;
using Microsoft.AspNetCore.Components;

namespace EggLedger.Web.Tests.Styles;

public class TileGroupTests : BunitContext {
    private static RenderFragment Text(string s) => b => b.AddContent(0, s);

    private static IReadOnlyList<TileGroup<int>.Tile> Tiles() => [
        new(1, Text("One")),
        new(2, Text("Two")),
        new(3, Text("Three"), Disabled: true)
    ];

    [Fact]
    public void Marks_selected_tile_active() {
        var cut = Render<TileGroup<int>>(p => p.Add(x => x.Options, Tiles()).Add(x => x.Selected, 2));
        var buttons = cut.FindAll("button");
        Assert.DoesNotContain("tiles-item-active", buttons[0].ClassList);
        Assert.Contains("tiles-item-active", buttons[1].ClassList);
    }

    [Fact]
    public void Click_raises_select_with_value() {
        var picked = 0;
        var cut = Render<TileGroup<int>>(p => p.Add(x => x.Options, Tiles()).Add(x => x.OnSelect, v => picked = v));
        cut.FindAll("button")[0].Click();
        Assert.Equal(1, picked);
    }

    [Fact]
    public void Disabled_tile_does_not_raise_select() {
        var picked = 0;
        var cut = Render<TileGroup<int>>(p => p.Add(x => x.Options, Tiles()).Add(x => x.OnSelect, v => picked = v));
        var disabled = cut.FindAll("button")[2];
        Assert.Contains("tiles-item-disabled", disabled.ClassList);
        disabled.Click();
        Assert.Equal(0, picked);
    }
}
