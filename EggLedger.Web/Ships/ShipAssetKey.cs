using System.Collections.Frozen;
using Ei;

namespace EggLedger.Web.Ships;

public static class ShipAssetKey {
    private static readonly string[] All = Enum.GetNames<MissionInfo.Spaceship>();
    private static readonly FrozenSet<string> Known = All.ToFrozenSet(StringComparer.Ordinal);

    public static IReadOnlyList<string> AllKeys => All;

    public static string For(MissionInfo.Spaceship ship) =>
        Enum.GetName(ship)
            ?? throw new ArgumentOutOfRangeException(nameof(ship), ship, "undefined Spaceship value");

    public static bool IsKnown(string key) => Known.Contains(key);
}
