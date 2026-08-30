using EggLedger.Domain.MissionPacking;
using Ei;
using Xunit;

namespace EggLedger.Domain.Tests.MissionPacking;

public sealed class MissionFuelsTests {
    [Fact]
    public void Build_MapsEggAndAmountInOrder() {
        var resp = new CompleteMissionResponse {
            Info = new MissionInfo(),
        };
        resp.Info.Fuels.Add(new MissionInfo.Fuel { Egg = Egg.Edible, Amount = 1000 });
        resp.Info.Fuels.Add(new MissionInfo.Fuel { Egg = Egg.Superfood, Amount = 42.5 });

        var result = MissionFuels.Build(resp);

        Assert.Equal(2, result.Count);
        Assert.Equal(new FuelEntry(0, (int)Egg.Edible, 1000), result[0]);
        Assert.Equal(new FuelEntry(1, (int)Egg.Superfood, 42.5), result[1]);
    }

    [Fact]
    public void Build_NoInfo_ReturnsEmpty() {
        var resp = new CompleteMissionResponse();
        Assert.Empty(MissionFuels.Build(resp));
    }
}
