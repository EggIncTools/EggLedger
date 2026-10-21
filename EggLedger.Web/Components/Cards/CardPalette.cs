using EggIdentity.UI.Canvas;
using EggLedger.Web.Missions.Model;

namespace EggLedger.Web.Components.Cards;

public static class CardPalette {
    public static IReadOnlyList<CanvasPaletteEntry> Entries { get; } = [
        new(CardKinds.Field, "Field", "type", 4, 1, """{"field":"ship"}"""),
        new(CardKinds.Label, "Label", "tag", 4, 1, """{"text":"Label"}"""),
        new(CardKinds.ShipImage, "Ship", "rocket", 1, 1),
        new(CardKinds.TargetImage, "Target", "map-pin", 1, 1),
        new(CardKinds.Stars, "Stars", "star", 2, 1),
        new(CardKinds.CapBadge, "Capacity badge", "shield-check", 2, 1),
        new(CardKinds.DropStrip, "Drop strip", "layout-grid", 6, 1, """{"top":6}"""),
        new(CardKinds.RarityBar, "Rarity bar", "chart-bar", 4, 1),
        new(CardKinds.DropTypeBars, "Drop types", "chart-column-big", 4, 1),
        new(CardKinds.Expected, "Expected", "activity", 2, 2),
        new(CardKinds.Box, "Box", "square", 6, 2),
        new(CardKinds.Divider, "Divider", "minus", 6, 1, """{"vertical":false}"""),
    ];
}
