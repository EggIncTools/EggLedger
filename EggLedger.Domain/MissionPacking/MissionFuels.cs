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
        var result = new List<FuelEntry>(fuels.Count);
        for (int i = 0; i < fuels.Count; i++) {
            result.Add(new FuelEntry(i, (int)fuels[i].Egg, fuels[i].Amount));
        }
        return result;
    }
}
