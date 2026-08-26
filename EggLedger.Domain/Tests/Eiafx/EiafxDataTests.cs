using EggLedger.Domain.Eiafx;
using EggLedger.Domain.Reports;

namespace EggLedger.Domain.Tests.Eiafx;

public class EiafxDataTests {
    [Fact]
    public void CraftingWeights_BaseItem_IsOne() {
        Assert.Equal(1.0, EiafxData.CraftingWeights[(2, 0)]);
    }



    [Fact]
    public void CraftingWeights_SelfContained_Is20() {
        Assert.Equal(20.0, EiafxData.CraftingWeights[(1, 0)]);
    }



    [Fact]
    public void CraftingWeights_CrossFamily_Is23() {
        Assert.Equal(23.0, EiafxData.CraftingWeights[(23, 2)]);
    }



    [Fact]
    public void CraftingWeights_SolarTitaniumChain() {
        Assert.Equal(10.0, EiafxData.CraftingWeights[(43, 1)]);
        Assert.Equal(120.0, EiafxData.CraftingWeights[(43, 2)]);
    }



    [Fact]
    public void CraftingWeights_LunarTotemT3_Is118() {
        Assert.Equal(118.0, EiafxData.CraftingWeights[(0, 3)]);
    }


    [Fact]
    public void FamilyAfxIds_TachyonStone() {
        Assert.True(EiafxData.FamilyAfxIds.TryGetValue("tachyon-stone", out var ids));
        Assert.Equal(new[] { 1, 2 }, ids);
    }

    [Fact]
    public void Families_FirstIsPuzzleCube() {
        Assert.Equal("puzzle-cube", EiafxData.Families[0].Id);
        Assert.Equal("Puzzle cube", EiafxData.Families[0].Name);
    }


    [Fact]
    public void EiafxWeightData_CraftingWeight_FallsBackToOne() {
        var wd = EiafxWeightData.Instance;
        Assert.Equal(20.0, wd.CraftingWeight(1, 0));
        Assert.Equal(1.0, wd.CraftingWeight(2, 0));

        Assert.Equal(1.0, wd.CraftingWeight(9999, 9));
    }

    [Fact]
    public void EiafxWeightData_FamilyAfxIds_KnownAndUnknown() {
        var wd = EiafxWeightData.Instance;
        Assert.Equal(new[] { 1, 2 }, wd.FamilyAfxIds("tachyon-stone"));
        Assert.Empty(wd.FamilyAfxIds("does-not-exist"));
    }
}
