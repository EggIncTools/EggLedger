using EggLedger.Domain.MissionPacking;
using Ei;
using Xunit;

namespace EggLedger.Domain.Tests.MissionPacking;

public sealed class ShipFuelCostsTests {
    [Fact]
    public void For_ChickenOneShort_ReturnsSingleRocketFuelEntry() {
        var result = ShipFuelCosts.For(MissionInfo.Spaceship.ChickenOne, MissionInfo.DurationType.Short);

        Assert.Equal([new FuelEntry(0, (int)Egg.RocketFuel, 2_000_000)], result);
    }

    [Fact]
    public void For_BcrEpic_ReturnsThreeEntriesInOrder() {
        var result = ShipFuelCosts.For(MissionInfo.Spaceship.Bcr, MissionInfo.DurationType.Epic);

        Assert.Equal([
            new FuelEntry(0, (int)Egg.Superfood, 5_000_000),
            new FuelEntry(1, (int)Egg.RocketFuel, 300_000_000),
            new FuelEntry(2, (int)Egg.Fusion, 100_000_000),
        ], result);
    }

    [Fact]
    public void For_HenerpriseLong_ReturnsThreeEntriesInOrder() {
        var result = ShipFuelCosts.For(MissionInfo.Spaceship.Henerprise, MissionInfo.DurationType.Long);

        Assert.Equal([
            new FuelEntry(0, (int)Egg.Dilithium, 3_000_000_000_000),
            new FuelEntry(1, (int)Egg.Antimatter, 3_000_000_000_000),
            new FuelEntry(2, (int)Egg.DarkMatter, 3_000_000_000_000),
        ], result);
    }

    [Fact]
    public void For_AtreggiesEpic_ReturnsFourEntriesInOrder() {
        var result = ShipFuelCosts.For(MissionInfo.Spaceship.Atreggies, MissionInfo.DurationType.Epic);

        Assert.Equal([
            new FuelEntry(0, (int)Egg.Tachyon, 2_000_000_000_000),
            new FuelEntry(1, (int)Egg.Dilithium, 6_000_000_000_000),
            new FuelEntry(2, (int)Egg.Antimatter, 6_000_000_000_000),
            new FuelEntry(3, (int)Egg.DarkMatter, 6_000_000_000_000),
        ], result);
    }

    [Fact]
    public void For_Tutorial_ReturnsEmpty() {
        Assert.Empty(ShipFuelCosts.For(MissionInfo.Spaceship.ChickenOne, MissionInfo.DurationType.Tutorial));
    }

    [Fact]
    public void For_UnknownShip_ReturnsEmpty() {
        Assert.Empty(ShipFuelCosts.For(999, (long)MissionInfo.DurationType.Short));
    }

    [Fact]
    public void TotalFor_SumsAllEggTypes() {
        var total = ShipFuelCosts.TotalFor(
            (long)MissionInfo.Spaceship.ChickenHeavy, (long)MissionInfo.DurationType.Long);

        Assert.Equal(55_000_000, total);
    }

    [Fact]
    public void TotalFor_Tutorial_IsZero() {
        Assert.Equal(0, ShipFuelCosts.TotalFor(
            (long)MissionInfo.Spaceship.Henerprise, (long)MissionInfo.DurationType.Tutorial));
    }
}
