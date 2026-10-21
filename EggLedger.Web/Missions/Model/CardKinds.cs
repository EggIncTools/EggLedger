using System.Text.Json;
using EggIdentity.Canvas;

namespace EggLedger.Web.Missions.Model;

public static class CardKinds {
    public const string Field = "field";
    public const string Label = "label";
    public const string ShipImage = "ship";
    public const string TargetImage = "target";
    public const string Stars = "stars";
    public const string CapBadge = "cap_badge";
    public const string DropStrip = "drop_strip";
    public const string RarityBar = "rarity_bar";
    public const string DropTypeBars = "drop_types";
    public const string Expected = "expected";
    public const string Box = "box";
    public const string Divider = "divider";

    public static bool NeedsDrops(string kind) =>
        kind is DropStrip or RarityBar or DropTypeBars;

    public static bool NeedsDrops(CanvasDoc doc) =>
        doc.Items.Any(item => NeedsDrops(item.Kind));
}

public static class CardPayload {
    public static string? Field(CanvasItem item) {
        if (item.Payload is not { Length: > 0 } json) return null;
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("field", out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    public static string? Text(CanvasItem item) {
        if (item.Payload is not { Length: > 0 } json) return null;
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("text", out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    public static int? Top(CanvasItem item) {
        if (item.Payload is not { Length: > 0 } json) return null;
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("top", out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : null;
    }

    public static bool? Vertical(CanvasItem item) {
        if (item.Payload is not { Length: > 0 } json) return null;
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("vertical", out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;
    }
}
