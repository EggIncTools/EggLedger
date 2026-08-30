using Ei;

namespace EggLedger.Domain.MissionPacking;

public static class ShipFuelCosts {
    private static readonly Dictionary<(long Ship, long DurationType), IReadOnlyList<FuelEntry>> Table = BuildTable();

    public static IReadOnlyList<FuelEntry> For(long ship, long durationType) =>
        Table.TryGetValue((ship, durationType), out var entries) ? entries : [];

    public static IReadOnlyList<FuelEntry> For(MissionInfo.Spaceship ship, MissionInfo.DurationType durationType) =>
        For((long)ship, (long)durationType);

    public static double TotalFor(long ship, long durationType) {
        double total = 0;
        foreach (var entry in For(ship, durationType)) {
            total += entry.Amount;
        }
        return total;
    }

    private static Dictionary<(long Ship, long DurationType), IReadOnlyList<FuelEntry>> BuildTable() {
        var table = new Dictionary<(long Ship, long DurationType), IReadOnlyList<FuelEntry>>();

        Add(table, MissionInfo.Spaceship.ChickenOne, MissionInfo.DurationType.Short, (Egg.RocketFuel, 2_000_000d));
        Add(table, MissionInfo.Spaceship.ChickenOne, MissionInfo.DurationType.Long, (Egg.RocketFuel, 3_000_000d));
        Add(table, MissionInfo.Spaceship.ChickenOne, MissionInfo.DurationType.Epic, (Egg.RocketFuel, 10_000_000d));

        Add(table, MissionInfo.Spaceship.ChickenNine, MissionInfo.DurationType.Short, (Egg.RocketFuel, 10_000_000d));
        Add(table, MissionInfo.Spaceship.ChickenNine, MissionInfo.DurationType.Long, (Egg.RocketFuel, 15_000_000d));
        Add(table, MissionInfo.Spaceship.ChickenNine, MissionInfo.DurationType.Epic, (Egg.RocketFuel, 25_000_000d));

        Add(table, MissionInfo.Spaceship.ChickenHeavy, MissionInfo.DurationType.Short, (Egg.RocketFuel, 100_000_000d));
        Add(table, MissionInfo.Spaceship.ChickenHeavy, MissionInfo.DurationType.Long, (Egg.RocketFuel, 50_000_000d), (Egg.Fusion, 5_000_000d));
        Add(table, MissionInfo.Spaceship.ChickenHeavy, MissionInfo.DurationType.Epic, (Egg.RocketFuel, 75_000_000d), (Egg.Fusion, 25_000_000d));

        Add(table, MissionInfo.Spaceship.Bcr, MissionInfo.DurationType.Short, (Egg.RocketFuel, 250_000_000d), (Egg.Fusion, 50_000_000d));
        Add(table, MissionInfo.Spaceship.Bcr, MissionInfo.DurationType.Long, (Egg.RocketFuel, 400_000_000d), (Egg.Fusion, 75_000_000d));
        Add(table, MissionInfo.Spaceship.Bcr, MissionInfo.DurationType.Epic, (Egg.Superfood, 5_000_000d), (Egg.RocketFuel, 300_000_000d), (Egg.Fusion, 100_000_000d));

        Add(table, MissionInfo.Spaceship.MilleniumChicken, MissionInfo.DurationType.Short, (Egg.Fusion, 5_000_000_000d), (Egg.Graviton, 1_000_000_000d));
        Add(table, MissionInfo.Spaceship.MilleniumChicken, MissionInfo.DurationType.Long, (Egg.Fusion, 7_000_000_000d), (Egg.Graviton, 5_000_000_000d));
        Add(table, MissionInfo.Spaceship.MilleniumChicken, MissionInfo.DurationType.Epic, (Egg.Superfood, 10_000_000d), (Egg.Fusion, 10_000_000_000d), (Egg.Graviton, 15_000_000_000d));

        Add(table, MissionInfo.Spaceship.CorellihenCorvette, MissionInfo.DurationType.Short, (Egg.Fusion, 15_000_000_000d), (Egg.Graviton, 2_000_000_000d));
        Add(table, MissionInfo.Spaceship.CorellihenCorvette, MissionInfo.DurationType.Long, (Egg.Fusion, 20_000_000_000d), (Egg.Graviton, 3_000_000_000d));
        Add(table, MissionInfo.Spaceship.CorellihenCorvette, MissionInfo.DurationType.Epic, (Egg.Superfood, 500_000_000d), (Egg.Fusion, 25_000_000_000d), (Egg.Graviton, 5_000_000_000d));

        Add(table, MissionInfo.Spaceship.Galeggtica, MissionInfo.DurationType.Short, (Egg.Fusion, 50_000_000_000d), (Egg.Graviton, 10_000_000_000d));
        Add(table, MissionInfo.Spaceship.Galeggtica, MissionInfo.DurationType.Long, (Egg.Fusion, 75_000_000_000d), (Egg.Graviton, 25_000_000_000d));
        Add(table, MissionInfo.Spaceship.Galeggtica, MissionInfo.DurationType.Epic, (Egg.Fusion, 100_000_000_000d), (Egg.Graviton, 50_000_000_000d), (Egg.Antimatter, 1_000_000_000d));

        Add(table, MissionInfo.Spaceship.Chickfiant, MissionInfo.DurationType.Short, (Egg.Dilithium, 200_000_000_000d), (Egg.Antimatter, 50_000_000_000d));
        Add(table, MissionInfo.Spaceship.Chickfiant, MissionInfo.DurationType.Long, (Egg.Dilithium, 250_000_000_000d), (Egg.Antimatter, 150_000_000_000d));
        Add(table, MissionInfo.Spaceship.Chickfiant, MissionInfo.DurationType.Epic, (Egg.Tachyon, 25_000_000_000d), (Egg.Dilithium, 250_000_000_000d), (Egg.Antimatter, 250_000_000_000d));

        Add(table, MissionInfo.Spaceship.Voyegger, MissionInfo.DurationType.Short, (Egg.Dilithium, 1_000_000_000_000d), (Egg.Antimatter, 1_000_000_000_000d));
        Add(table, MissionInfo.Spaceship.Voyegger, MissionInfo.DurationType.Long, (Egg.Dilithium, 1_500_000_000_000d), (Egg.Antimatter, 1_500_000_000_000d));
        Add(table, MissionInfo.Spaceship.Voyegger, MissionInfo.DurationType.Epic, (Egg.Tachyon, 100_000_000_000d), (Egg.Dilithium, 2_000_000_000_000d), (Egg.Antimatter, 2_000_000_000_000d));

        Add(table, MissionInfo.Spaceship.Henerprise, MissionInfo.DurationType.Short, (Egg.Dilithium, 2_000_000_000_000d), (Egg.Antimatter, 2_000_000_000_000d));
        Add(table, MissionInfo.Spaceship.Henerprise, MissionInfo.DurationType.Long, (Egg.Dilithium, 3_000_000_000_000d), (Egg.Antimatter, 3_000_000_000_000d), (Egg.DarkMatter, 3_000_000_000_000d));
        Add(table, MissionInfo.Spaceship.Henerprise, MissionInfo.DurationType.Epic, (Egg.Tachyon, 1_000_000_000_000d), (Egg.Dilithium, 3_000_000_000_000d), (Egg.Antimatter, 3_000_000_000_000d), (Egg.DarkMatter, 3_000_000_000_000d));

        Add(table, MissionInfo.Spaceship.Atreggies, MissionInfo.DurationType.Short, (Egg.Dilithium, 4_000_000_000_000d), (Egg.Antimatter, 4_000_000_000_000d), (Egg.DarkMatter, 3_000_000_000_000d));
        Add(table, MissionInfo.Spaceship.Atreggies, MissionInfo.DurationType.Long, (Egg.Dilithium, 6_000_000_000_000d), (Egg.Antimatter, 6_000_000_000_000d), (Egg.DarkMatter, 4_000_000_000_000d));
        Add(table, MissionInfo.Spaceship.Atreggies, MissionInfo.DurationType.Epic, (Egg.Tachyon, 2_000_000_000_000d), (Egg.Dilithium, 6_000_000_000_000d), (Egg.Antimatter, 6_000_000_000_000d), (Egg.DarkMatter, 6_000_000_000_000d));

        return table;
    }

    private static void Add(
        Dictionary<(long Ship, long DurationType), IReadOnlyList<FuelEntry>> table,
        MissionInfo.Spaceship ship,
        MissionInfo.DurationType durationType,
        params (Egg Egg, double Amount)[] fuels) {
        var entries = new FuelEntry[fuels.Length];
        for (int i = 0; i < fuels.Length; i++) {
            entries[i] = new FuelEntry(i, (int)fuels[i].Egg, fuels[i].Amount);
        }
        table[((long)ship, (long)durationType)] = entries;
    }
}
