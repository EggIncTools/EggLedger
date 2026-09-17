using Ei;

namespace EggLedger.Web.Ships;

public static class ShipAssetKey {
    private static readonly string[] _all = Enum.GetNames<MissionInfo.Spaceship>();
    private static readonly HashSet<string> _known = new(_all, StringComparer.Ordinal);

    public static IReadOnlyList<string> AllKeys => _all;

    public static string For(MissionInfo.Spaceship ship) =>
        Enum.GetName(ship)
            ?? throw new ArgumentOutOfRangeException(nameof(ship), ship, "undefined Spaceship value");

    public static bool IsKnown(string key) => _known.Contains(key);
}
