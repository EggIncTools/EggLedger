namespace EggLedger.Web.Missions.Model;

public sealed record MissionCardConfig(
    string Name,
    CardLayout Layout,
    IReadOnlyList<CardFieldSlot> Fields,
    bool ColorizeByDuration,
    bool ShowExpectedDropsPerShip,
    string? BackgroundColor,
    string? BorderColor,
    string? AccentColor,
    string? CustomCss) {
    public static readonly MissionCardConfig Classic = new(
        "Classic",
        CardLayout.CompactRow,
        [
            new CardFieldSlot(CardField.LaunchDate, true, 0),
            new CardFieldSlot(CardField.LaunchTime, true, 1),
            new CardFieldSlot(CardField.ShipIcon, true, 2),
            new CardFieldSlot(CardField.ShipName, true, 3),
            new CardFieldSlot(CardField.LevelStars, true, 4),
            new CardFieldSlot(CardField.Target, true, 5),
            new CardFieldSlot(CardField.Duration, false, 6),
            new CardFieldSlot(CardField.Capacity, false, 7),
            new CardFieldSlot(CardField.CapacityModifierBadge, false, 8),
            new CardFieldSlot(CardField.DropCount, true, 9)
        ],
        true,
        true,
        null,
        null,
        null,
        null);

    public static readonly MissionCardConfig CalendarDay = Calendar(
        "CalendarDay",
        CardField.ShipIcon, CardField.ShipName, CardField.LevelStars, CardField.Target, CardField.Duration, CardField.LaunchTime, CardField.CapacityModifierBadge);

    public static readonly MissionCardConfig CalendarWeek = Calendar(
        "CalendarWeek",
        CardField.ShipIcon, CardField.ShipName, CardField.Target, CardField.Duration);

    public static readonly MissionCardConfig CalendarMonth = Calendar(
        "CalendarMonth",
        CardField.ShipIcon, CardField.Target);

    public static readonly MissionCardConfig Agenda = Calendar(
        "Agenda",
        CardField.ShipIcon, CardField.ShipName, CardField.LevelStars, CardField.Target, CardField.Duration, CardField.LaunchDate, CardField.LaunchTime, CardField.CapacityModifierBadge);

    private static MissionCardConfig Calendar(string name, params CardField[] fields) =>
        new(name, CardLayout.Cards, [.. fields.Select((f, i) => new CardFieldSlot(f, true, i))], true, false, null, null, null, null);

    public bool UsesCards => Layout == CardLayout.Cards;

    public MissionCardConfig WithAllFields() {
        var missing = Enum.GetValues<CardField>().Where(f => Fields.All(s => s.Field != f)).ToList();
        if (missing.Count == 0) return this;

        var next = Fields.Count == 0 ? 0 : Fields.Max(s => s.Order) + 1;
        return this with { Fields = [.. Fields, .. missing.Select((f, i) => new CardFieldSlot(f, true, next + i))] };
    }

    public bool Equals(MissionCardConfig? other) =>
        other is not null
        && Name == other.Name
        && Layout == other.Layout
        && ColorizeByDuration == other.ColorizeByDuration
        && ShowExpectedDropsPerShip == other.ShowExpectedDropsPerShip
        && BackgroundColor == other.BackgroundColor
        && BorderColor == other.BorderColor
        && AccentColor == other.AccentColor
        && CustomCss == other.CustomCss
        && Fields.SequenceEqual(other.Fields);

    public override int GetHashCode() => HashCode.Combine(Name, Layout, ColorizeByDuration, Fields.Count);
}

public sealed record CardFieldSlot(CardField Field, bool Enabled, int Order);

public enum CardLayout {
    CompactRow,
    Grid,
    Cards
}

public enum CardField {
    ShipName,
    LevelStars,
    LaunchDate,
    LaunchTime,
    Duration,
    Target,
    Capacity,
    CapacityModifierBadge,
    DropCount,
    ShipIcon
}

public sealed record CardPresetSet(IReadOnlyList<MissionCardConfig> Presets, string ActivePresetName) {
    public static readonly CardPresetSet Default = new([MissionCardConfig.Classic], "Classic");

    public bool Equals(CardPresetSet? other) =>
        other is not null && ActivePresetName == other.ActivePresetName && Presets.SequenceEqual(other.Presets);

    public override int GetHashCode() => HashCode.Combine(ActivePresetName, Presets.Count);

    public MissionCardConfig ActivePreset() =>
        (Presets.FirstOrDefault(p => p.Name == ActivePresetName) ?? MissionCardConfig.Classic).WithAllFields();
}
