using Ei;

namespace EggLedger.Domain.MissionPacking;

public readonly record struct FuelEntry(int FuelIndex, int EggId, double Amount);

public static class MissionFuels {
    public static List<FuelEntry> Build(CompleteMissionResponse resp) {
        ArgumentNullException.ThrowIfNull(resp);
        var fuels = resp.Info?.Fuels;
        if (fuels is null || fuels.Count == 0) {
            return [];
        }
        return [.. fuels.Select((f, i) => new FuelEntry(i, (int)f.Egg, f.Amount))];
    }
}
