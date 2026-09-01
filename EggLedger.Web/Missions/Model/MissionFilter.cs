namespace EggLedger.Web.Missions.Model;

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

public sealed record DropFilterValue(DropMatch Match) : FilterValue;

public sealed record DropMatch(int? Name, int? Level, int? Rarity, double? Quality = null) {
    public static readonly DropMatch Any = new(null, null, null);
    public static DropMatch AnyOfRarity(int rarity) => new(null, null, rarity);
}
