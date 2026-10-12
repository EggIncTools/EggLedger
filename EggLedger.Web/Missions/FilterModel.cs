namespace EggLedger.Web.Missions;

public sealed class PickerOption {
    public string Text { get; set; } = "";
    public string Value { get; set; } = "";
    public string? StyleClass { get; set; }
    public string? ImagePath { get; set; }
    public int? Rarity { get; set; }
    public string? GroupKey { get; set; }
    public string? GroupLabel { get; set; }
    public string? Badge { get; set; }
    public string? TierKey { get; set; }

    public FilterOption ToFilterOption() =>
        new(Value, Text) { Class = StyleClass, Image = ImagePath, Badge = Badge, Group = GroupLabel };
}
