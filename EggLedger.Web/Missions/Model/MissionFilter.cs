namespace EggLedger.Web.Missions.Model;

public sealed record MissionFilter(IReadOnlyList<FilterGroup> Groups) {
    public static readonly MissionFilter Empty = new([]);

    public bool IsEmpty => Groups.Count == 0 || Groups.All(g => g.Conditions.Count == 0);
}

public sealed record FilterGroup(IReadOnlyList<Condition> Conditions);

public sealed record Condition(FilterField Field, FilterOperator Operator, FilterValue Value);

public enum FilterField {
    Ship,
    DurationType,
    Level,
    Capacity,
    Target,
    MissionType,
    LaunchDate,
    ReturnDate,
    DubCap,
    BuggedCap,
    Drops,
}

public enum FilterValueType {
    Enum,
    Numeric,
    Date,
    Bool,
    Target,
    Drop,
}

public enum FilterOperator {
    Equals,
    NotEquals,
    Greater,
    Less,
    GreaterOrEqual,
    LessOrEqual,
    Contains,
    NotContains,
    IsTrue,
    IsFalse,
}

public abstract record FilterValue {
    public sealed record EnumValue(int Code) : FilterValue;
    public sealed record Number(double N) : FilterValue;
    public sealed record Day(DateOnly Date) : FilterValue;

    public sealed record Flag(bool On) : FilterValue;
    public sealed record Drop(DropMatch Match) : FilterValue;

    public sealed record None : FilterValue {
        public static readonly None Instance = new();
    }
}

public sealed record DropMatch(int? Name, int? Level, int? Rarity, double? Quality = null) {
    public static readonly DropMatch Any = new(null, null, null);
    public static DropMatch AnyOfRarity(int rarity) => new(null, null, rarity);
}
