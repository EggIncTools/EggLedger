using System.Globalization;
using Ei;
using EggIdentity.Canvas;
using EggLedger.Domain.MissionPacking;
using EggLedger.Domain.MissionQuery;

namespace EggLedger.Web.Missions.Model;

public sealed record MissionCardData(DatabaseMission Mission, TimeZoneInfo Tz, int? DropCount, MissionDropSummary? Drops, double? ExpectedDrops);

public sealed record MissionDropSummary(int Artifacts, int Stones, int Ingredients, int Fragments, IReadOnlyList<int> RarityCounts, IReadOnlyList<MissionDrop> Top) {
    public static MissionDropSummary From(IReadOnlyList<MissionDrop> drops, int top) {
        var counts = new int[4];
        var artifacts = 0;
        var stones = 0;
        var ingredients = 0;
        var fragments = 0;
        foreach (var drop in drops) {
            if (drop.Rarity is >= 0 and < 4) counts[drop.Rarity]++;
            switch (drop.SpecType) {
                case "Artifact":
                    artifacts++;
                    break;
                case "Stone":
                    stones++;
                    break;
                case "Ingredient":
                    ingredients++;
                    break;
                case "StoneFragment":
                    fragments++;
                    break;
            }
        }

        var topDrops = drops.OrderByDescending(d => d.Rarity).ThenBy(d => d.IVOrder).Take(top).ToList();
        return new MissionDropSummary(artifacts, stones, ingredients, fragments, counts, topDrops);
    }
}

public static class MissionFields {
    public const string Ship = "ship";
    public const string Level = "level";
    public const string Launch = "launch";
    public const string LaunchDate = "launch_date";
    public const string LaunchTime = "launch_time";
    public const string Return = "return";
    public const string Duration = "duration";
    public const string DurationType = "duration_type";
    public const string Target = "target";
    public const string Capacity = "capacity";
    public const string CapModifier = "cap_modifier";
    public const string DropCount = "drop_count";
    public const string Artifacts = "drops_artifacts";
    public const string Stones = "drops_stones";
    public const string Ingredients = "drops_ingredients";
    public const string Fragments = "drops_fragments";

    private const string DateTimeFormat = "yyyy-MM-dd HH:mm:ss";
    private const string DateFormat = "yyyy-MM-dd";
    private const string TimeFormat = "HH:mm:ss";

    public static BindingCatalog Catalog { get; } = new BindingCatalog([
        new BindableField(Ship, "Ship", typeof(string), BindingRenderer.Text),
        new BindableField(Level, "Level", typeof(int), BindingRenderer.Number),
        new BindableField(Launch, "Launch", typeof(DateTime), BindingRenderer.Text) { Format = FormatDateTime },
        new BindableField(LaunchDate, "Launch date", typeof(DateTime), BindingRenderer.Text) { Format = FormatDate },
        new BindableField(LaunchTime, "Launch time", typeof(DateTime), BindingRenderer.Text) { Format = FormatTime },
        new BindableField(Return, "Return", typeof(DateTime), BindingRenderer.Text) { Format = FormatDateTime },
        new BindableField(Duration, "Duration", typeof(string), BindingRenderer.Text),
        new BindableField(DurationType, "Duration type", typeof(MissionInfo.DurationType), BindingRenderer.Text),
        new BindableField(Target, "Target", typeof(string), BindingRenderer.Text),
        new BindableField(Capacity, "Capacity", typeof(int), BindingRenderer.Number),
        new BindableField(CapModifier, "Capacity modifier", typeof(string), BindingRenderer.Text),
        new BindableField(DropCount, "Drop count", typeof(int), BindingRenderer.Number),
        new BindableField(Artifacts, "Artifacts", typeof(int), BindingRenderer.Number),
        new BindableField(Stones, "Stones", typeof(int), BindingRenderer.Number),
        new BindableField(Ingredients, "Ingredients", typeof(int), BindingRenderer.Number),
        new BindableField(Fragments, "Fragments", typeof(int), BindingRenderer.Number),
    ]);

    public static object? Resolve(string id, MissionCardData data) => id switch {
        Ship => data.Mission.ShipString,
        Level => data.Mission.Level,
        Launch => MissionFilterMatcher.LedgerDate(data.Mission.LaunchDT, data.Tz),
        LaunchDate => MissionFilterMatcher.LedgerDate(data.Mission.LaunchDT, data.Tz),
        LaunchTime => MissionFilterMatcher.LedgerDate(data.Mission.LaunchDT, data.Tz),
        Return => MissionFilterMatcher.LedgerDate(data.Mission.ReturnDT, data.Tz),
        Duration => data.Mission.DurationString,
        DurationType => data.Mission.DurationType,
        Target => NameFormatting.TargetDisplayName(data.Mission.Target),
        Capacity => data.Mission.Capacity,
        CapModifier => ResolveCapModifier(data.Mission),
        DropCount => data.DropCount,
        Artifacts => data.Drops?.Artifacts,
        Stones => data.Drops?.Stones,
        Ingredients => data.Drops?.Ingredients,
        Fragments => data.Drops?.Fragments,
        _ => null,
    };

    public static string Format(string id, MissionCardData data) => Catalog.Format(id, Resolve(id, data));

    private static string? ResolveCapModifier(DatabaseMission mission) {
        if (mission.IsBuggedCap) return "0.6x";
        if (mission.IsDubCap) return "2x";
        return null;
    }

    private static string FormatDateTime(object? value) =>
        value is DateTime dt ? dt.ToString(DateTimeFormat, CultureInfo.InvariantCulture) : "-";

    private static string FormatDate(object? value) =>
        value is DateTime dt ? dt.ToString(DateFormat, CultureInfo.InvariantCulture) : "-";

    private static string FormatTime(object? value) =>
        value is DateTime dt ? dt.ToString(TimeFormat, CultureInfo.InvariantCulture) : "-";
}
