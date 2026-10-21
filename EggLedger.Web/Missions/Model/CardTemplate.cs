using System.Text.Json;
using System.Text.Json.Nodes;
using EggIdentity.Canvas;

namespace EggLedger.Web.Missions.Model;

public enum CardSlot {
    List,
    CalendarDay,
    CalendarWeek,
    CalendarMonth,
    Agenda
}

public sealed record CardTemplate(string Name, CanvasDoc Doc, string? CustomCss = null, bool ColorizeByDuration = true);

public sealed record CardTemplateSet(IReadOnlyList<CardTemplate> Templates, IReadOnlyDictionary<CardSlot, string> Slots) {
    public const int Version = 1;

    public static CardTemplateSet Default { get; } = new(CardBuiltins.All, Enum.GetValues<CardSlot>().ToDictionary(s => s, CardBuiltins.NameFor));

    public CardTemplate For(CardSlot slot) =>
        (Slots.TryGetValue(slot, out var name) ? Find(name) : null)
        ?? Find(CardBuiltins.NameFor(slot))
        ?? CardBuiltins.For(slot);

    public CardTemplateSet With(CardTemplate template) {
        ArgumentNullException.ThrowIfNull(template);
        var list = Templates.ToList();
        var index = list.FindIndex(t => t.Name == template.Name);
        if (index >= 0) list[index] = template;
        else list.Add(template);
        return this with { Templates = list };
    }

    public CardTemplateSet Without(string name) {
        if (IsBuiltin(name) || Find(name) is null) return this;
        return new CardTemplateSet(
            [.. Templates.Where(t => t.Name != name)],
            Slots.ToDictionary(p => p.Key, p => p.Value == name ? CardBuiltins.NameFor(p.Key) : p.Value));
    }

    public CardTemplateSet Assign(CardSlot slot, string name) {
        if (Find(name) is null) return this;
        return this with { Slots = new Dictionary<CardSlot, string>(Slots) { [slot] = name } };
    }

    public static bool IsBuiltin(string name) => CardBuiltins.All.Any(t => t.Name == name);

    public bool Equals(CardTemplateSet? other) =>
        other is not null
        && Templates.SequenceEqual(other.Templates)
        && Slots.Count == other.Slots.Count
        && Slots.All(p => other.Slots.TryGetValue(p.Key, out var value) && value == p.Value);

    public override int GetHashCode() {
        var hash = new HashCode();
        foreach (var template in Templates) hash.Add(template);
        var slots = 0;
        foreach (var pair in Slots) slots = unchecked(slots + HashCode.Combine(pair.Key, pair.Value));
        hash.Add(slots);
        return hash.ToHashCode();
    }

    internal static CardTemplateSet Compose(IEnumerable<CardTemplate> stored, Func<CardSlot, string?> requested) {
        var set = stored.Aggregate(new CardTemplateSet([], new Dictionary<CardSlot, string>()), (s, t) => s.With(t));
        set = CardBuiltins.All.Aggregate(set, (s, b) => s.Find(b.Name) is null ? s.With(b) : s);
        var slots = Enum.GetValues<CardSlot>().ToDictionary(
            s => s,
            s => requested(s) is { } name && set.Find(name) is not null ? name : CardBuiltins.NameFor(s));
        return set with { Slots = slots };
    }

    private CardTemplate? Find(string? name) => name is null ? null : Templates.FirstOrDefault(t => t.Name == name);
}

public static class CardBuiltins {
    public static CardTemplate Classic { get; } = Build("Classic", 24, 2, [
        [F(MissionFields.LaunchDate, 3), F(MissionFields.LaunchTime, 3), K(CardKinds.ShipImage, 1), F(MissionFields.Ship, 5),
            K(CardKinds.Stars, 2), K(CardKinds.TargetImage, 1), F(MissionFields.Target, 5), F(MissionFields.DropCount, 2)],
    ]);

    public static CardTemplate CalendarDay { get; } = Build("CalendarDay", 12, 2, [
        [K(CardKinds.ShipImage, 1), F(MissionFields.Ship, 5), K(CardKinds.Stars, 2), K(CardKinds.CapBadge, 2), F(MissionFields.LaunchTime, 2)],
        [K(CardKinds.TargetImage, 1), F(MissionFields.Target, 5), F(MissionFields.Duration, 3)],
    ]);

    public static CardTemplate CalendarWeek { get; } = Build("CalendarWeek", 8, 1, [
        [K(CardKinds.ShipImage, 1), F(MissionFields.Ship, 4), K(CardKinds.TargetImage, 1), F(MissionFields.Duration, 2)],
    ]);

    public static CardTemplate CalendarMonth { get; } = Build("CalendarMonth", 4, 1, [
        [K(CardKinds.ShipImage, 2), K(CardKinds.TargetImage, 2)],
    ]);

    public static CardTemplate Agenda { get; } = Build("Agenda", 12, 3, [
        [K(CardKinds.ShipImage, 1), F(MissionFields.Ship, 6), K(CardKinds.Stars, 2), K(CardKinds.CapBadge, 3)],
        [K(CardKinds.TargetImage, 1), F(MissionFields.Target, 6), F(MissionFields.Duration, 5)],
        [F(MissionFields.LaunchDate, 4), F(MissionFields.LaunchTime, 4)],
    ]);

    internal static IReadOnlyList<CardTemplate> All { get; } = [Classic, CalendarDay, CalendarWeek, CalendarMonth, Agenda];

    internal static string NameFor(CardSlot slot) => For(slot).Name;

    internal static CardTemplate For(CardSlot slot) => slot switch {
        CardSlot.CalendarDay => CalendarDay,
        CardSlot.CalendarWeek => CalendarWeek,
        CardSlot.CalendarMonth => CalendarMonth,
        CardSlot.Agenda => Agenda,
        _ => Classic,
    };

    internal static string IdFor(string kind, string? field) => field ?? kind switch {
        CardKinds.ShipImage => "ship_img",
        CardKinds.TargetImage => "target_img",
        _ => kind,
    };

    internal static string? PayloadFor(string? field) => field is null ? null : "{\"field\":\"" + field + "\"}";

    private static (string Kind, string? Field, int W) F(string field, int w) => (CardKinds.Field, field, w);

    private static (string Kind, string? Field, int W) K(string kind, int w) => (kind, null, w);

    private static CardTemplate Build(string name, int cols, int rows, (string Kind, string? Field, int W)[][] layout) {
        var items = new List<CanvasItem>();
        for (var row = 0; row < layout.Length; row++) {
            var x = 1;
            foreach (var (kind, field, w) in layout[row]) {
                items.Add(new CanvasItem(IdFor(kind, field), kind, x, row + 1, w, 1, items.Count) { Payload = PayloadFor(field) });
                x += w;
            }
        }

        return new CardTemplate(name, new CanvasDoc(cols, rows, items));
    }
}

public static class CardTemplateCodec {
    public static string Serialize(CardTemplateSet set) {
        ArgumentNullException.ThrowIfNull(set);
        var templates = new JsonArray();
        foreach (var template in set.Templates) {
            templates.Add(new JsonObject {
                ["name"] = template.Name,
                ["doc"] = JsonNode.Parse(CanvasCodec.Serialize(template.Doc)),
                ["customCss"] = template.CustomCss,
                ["colorizeByDuration"] = template.ColorizeByDuration,
            });
        }

        var slots = new JsonObject();
        foreach (var slot in Enum.GetValues<CardSlot>()) {
            if (set.Slots.TryGetValue(slot, out var name)) slots[SlotKey(slot)] = name;
        }

        return new JsonObject {
            ["version"] = CardTemplateSet.Version,
            ["templates"] = templates,
            ["slots"] = slots,
        }.ToJsonString();
    }

    public static CardTemplateSet Deserialize(string json) {
        if (string.IsNullOrWhiteSpace(json)) return CardTemplateSet.Default;
        try {
            if (JsonNode.Parse(json) is not JsonObject root) return CardTemplateSet.Default;
            var version = 1;
            if (root["version"] is { } versionNode && !Int(versionNode, out version)) return CardTemplateSet.Default;
            if (version > CardTemplateSet.Version) return CardTemplateSet.Default;

            var templates = new List<CardTemplate>();
            if (root["templates"] is JsonArray entries) {
                foreach (var entry in entries) {
                    if (ReadTemplate(entry) is { } template) templates.Add(template);
                }
            }

            var slots = root["slots"] as JsonObject;
            return CardTemplateSet.Compose(templates, slot => Text(slots?[SlotKey(slot)]));
        } catch (Exception ex) when (Recoverable(ex)) {
            return CardTemplateSet.Default;
        }
    }

    private static CardTemplate? ReadTemplate(JsonNode? entry) {
        if (entry is not JsonObject obj) return null;
        if (Text(obj["name"]) is not { } name || string.IsNullOrWhiteSpace(name)) return null;
        if (obj["doc"] is not JsonObject docNode) return null;

        CanvasDoc doc;
        try {
            doc = CanvasCodec.Deserialize(docNode.ToJsonString());
        } catch (Exception ex) when (Recoverable(ex)) {
            return null;
        }

        var colorize = obj["colorizeByDuration"] is not JsonValue flag || !flag.TryGetValue<bool>(out var value) || value;
        return new CardTemplate(name, doc, Text(obj["customCss"]), colorize);
    }

    private static string SlotKey(CardSlot slot) => JsonNamingPolicy.CamelCase.ConvertName(slot.ToString());

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static bool Int(JsonNode node, out int result) {
        result = 0;
        return node is JsonValue value && value.TryGetValue(out result);
    }

    private static bool Recoverable(Exception ex) =>
        ex is JsonException or InvalidOperationException or FormatException or ArgumentException or NotSupportedException or OverflowException;
}

public static class LegacyCardUpgrade {
    private const int Cols = 24;
    private const int Rows = 2;

    public static CardTemplate FromConfig(MissionCardConfig legacy) {
        ArgumentNullException.ThrowIfNull(legacy);
        var items = new List<CanvasItem>();
        if (legacy.BackgroundColor is not null || legacy.BorderColor is not null) {
            items.Add(new CanvasItem("bg", CardKinds.Box, 1, 1, Cols, Rows, 0) {
                Style = new ItemStyle { Background = legacy.BackgroundColor, Border = legacy.BorderColor ?? legacy.AccentColor },
            });
        }

        var ids = new HashSet<string>(items.Select(i => i.Id), StringComparer.Ordinal);
        var x = 1;
        var y = 1;
        foreach (var slot in legacy.Fields.Where(f => f.Enabled).OrderBy(f => f.Order)) {
            var (kind, field) = Map(slot.Field);
            var w = Width(slot.Field);
            if (x + w - 1 > Cols) {
                y++;
                x = 1;
            }

            if (y > Rows) break;
            items.Add(new CanvasItem(UniqueId(ids, CardBuiltins.IdFor(kind, field)), kind, x, y, w, 1, items.Count) { Payload = CardBuiltins.PayloadFor(field) });
            x += w;
        }

        return new CardTemplate(legacy.Name, CanvasOps.Clean(new CanvasDoc(Cols, Rows, items)), legacy.CustomCss, legacy.ColorizeByDuration);
    }

    public static CardTemplateSet FromPresetSet(CardPresetSet legacy) {
        ArgumentNullException.ThrowIfNull(legacy);
        var converted = legacy.Presets.Where(p => p.Name != CardBuiltins.Classic.Name).Select(FromConfig).ToList();
        return CardTemplateSet.Compose(converted, slot => slot == CardSlot.List ? legacy.ActivePresetName : null);
    }

    private static (string Kind, string? Field) Map(CardField field) => field switch {
        CardField.LaunchDate => (CardKinds.Field, MissionFields.LaunchDate),
        CardField.LaunchTime => (CardKinds.Field, MissionFields.LaunchTime),
        CardField.ShipName => (CardKinds.Field, MissionFields.Ship),
        CardField.Duration => (CardKinds.Field, MissionFields.Duration),
        CardField.Target => (CardKinds.Field, MissionFields.Target),
        CardField.Capacity => (CardKinds.Field, MissionFields.Capacity),
        CardField.DropCount => (CardKinds.Field, MissionFields.DropCount),
        CardField.LevelStars => (CardKinds.Stars, null),
        CardField.ShipIcon => (CardKinds.ShipImage, null),
        CardField.CapacityModifierBadge => (CardKinds.CapBadge, null),
        _ => (CardKinds.Field, null),
    };

    private static int Width(CardField field) => field switch {
        CardField.LaunchDate or CardField.LaunchTime => 3,
        CardField.ShipName or CardField.Target => 5,
        _ => 2,
    };

    private static string UniqueId(HashSet<string> ids, string id) {
        var candidate = id;
        for (var n = 2; !ids.Add(candidate); n++) candidate = id + "_" + n;
        return candidate;
    }
}
