using EggLedger.Domain.Ei;
using Ei;

namespace EggLedger.Domain.Tests;

public class ArtifactExtensionsTests {
    [Fact]
    public void DropEffectString_KnownArtifact() {
        var spec = new ArtifactSpec {
            name = ArtifactSpec.Name.TachyonDeflector,
            level = ArtifactSpec.Level.Greater,
            rarity = ArtifactSpec.Rarity.Legendary,
        };
        Assert.Equal("[20%]", spec.DropEffectString());
    }

    [Fact]
    public void DropEffectString_UnknownArtifact() {
        var spec = new ArtifactSpec {
            name = ArtifactSpec.Name.Unknown,
            level = ArtifactSpec.Level.Inferior,
            rarity = ArtifactSpec.Rarity.Common,
        };
        Assert.Equal("", spec.DropEffectString());
    }

    [Fact]
    public void DropEffectString_NilLevel() {
        var spec = new ArtifactSpec {
            name = ArtifactSpec.Name.TachyonDeflector,
            rarity = ArtifactSpec.Rarity.Common,
        };
        Assert.Equal("", spec.DropEffectString());
    }

    [Fact]
    public void DropEffectString_NilName() {
        var spec = new ArtifactSpec {
            level = ArtifactSpec.Level.Inferior,
            rarity = ArtifactSpec.Rarity.Common,
        };
        Assert.Equal("", spec.DropEffectString());
    }

    [Theory]
    [InlineData(ArtifactSpec.Name.TachyonDeflector, ArtifactSpec.Level.Inferior, "WEAK")]
    [InlineData(ArtifactSpec.Name.TachyonDeflector, ArtifactSpec.Level.Greater, "EGGCEPTIONAL")]
    [InlineData(ArtifactSpec.Name.TachyonStoneFragment, ArtifactSpec.Level.Inferior, "FRAGMENT")]
    [InlineData(ArtifactSpec.Name.GoldMeteorite, ArtifactSpec.Level.Normal, "SOLID")]
    [InlineData(ArtifactSpec.Name.Unknown, ArtifactSpec.Level.Inferior, "?")]
    public void TierName(ArtifactSpec.Name name, ArtifactSpec.Level level, string want) {
        var spec = new ArtifactSpec { name = name, level = level };
        Assert.Equal(want, spec.TierName());
    }

    [Fact]
    public void TierName_NilLevel() {
        var spec = new ArtifactSpec { name = ArtifactSpec.Name.TachyonDeflector };
        Assert.Equal("?", spec.TierName());
    }

    [Fact]
    public void TierName_NilName() {
        var spec = new ArtifactSpec { level = ArtifactSpec.Level.Inferior };
        Assert.Equal("?", spec.TierName());
    }

    [Fact]
    public void GenericBenefitString_KnownArtifact() {
        var spec = new ArtifactSpec { name = ArtifactSpec.Name.QuantumMetronome };
        Assert.Equal("[+^b] egg laying rate", spec.GenericBenefitString());
    }

    [Fact]
    public void GenericBenefitString_Stone() {
        var spec = new ArtifactSpec { name = ArtifactSpec.Name.SoulStone };
        Assert.Equal("[+^b] bonus per Soul Egg", spec.GenericBenefitString());
    }

    [Fact]
    public void GenericBenefitString_Unknown() {
        var spec = new ArtifactSpec { name = ArtifactSpec.Name.Unknown };
        Assert.Equal("", spec.GenericBenefitString());
    }

    [Fact]
    public void GenericBenefitString_TungstenAnkhMatchesDemeter() {
        var ankh = new ArtifactSpec { name = ArtifactSpec.Name.TungstenAnkh };
        var necklace = new ArtifactSpec { name = ArtifactSpec.Name.DemetersNecklace };
        Assert.Equal(necklace.GenericBenefitString(), ankh.GenericBenefitString());
    }

    [Fact]
    public void GenericBenefitString_NilName() {
        var spec = new ArtifactSpec();
        Assert.Equal("", spec.GenericBenefitString());
    }

    [Theory]
    [InlineData(ArtifactSpec.Name.BookOfBasan, 33)]
    [InlineData(ArtifactSpec.Name.TachyonStone, 6)]
    [InlineData(ArtifactSpec.Name.Unknown, 0)]
    public void InventoryVisualizerOrder(ArtifactSpec.Name name, int want) =>
        Assert.Equal(want, name.InventoryVisualizerOrder());

    [Fact]
    public void InventoryVisualizerOrder_StoneFragmentMatchesStone() {
        var stone = ArtifactSpec.Name.TachyonStone.InventoryVisualizerOrder();
        var frag = ArtifactSpec.Name.TachyonStoneFragment.InventoryVisualizerOrder();
        Assert.Equal(stone, frag);
    }

    [Theory]
    [InlineData(ArtifactSpec.Name.BookOfBasan, ArtifactSpec.Type.Artifact)]
    [InlineData(ArtifactSpec.Name.TachyonStone, ArtifactSpec.Type.Stone)]
    [InlineData(ArtifactSpec.Name.TachyonStoneFragment, ArtifactSpec.Type.StoneIngredient)]
    [InlineData(ArtifactSpec.Name.GoldMeteorite, ArtifactSpec.Type.Ingredient)]
    [InlineData(ArtifactSpec.Name.Unknown, ArtifactSpec.Type.Artifact)]
    public void ArtifactType(ArtifactSpec.Name name, ArtifactSpec.Type want) =>
        Assert.Equal(want, name.ArtifactType());

    [Fact]
    public void CorrespondingStone() {
        Assert.Equal(
            ArtifactSpec.Name.TachyonStone,
            ArtifactSpec.Name.TachyonStoneFragment.CorrespondingStone());
    }

    [Fact]
    public void CorrespondingStone_NonFragment() {
        Assert.Equal(
            ArtifactSpec.Name.Unknown,
            ArtifactSpec.Name.TachyonStone.CorrespondingStone());
    }

    [Fact]
    public void CorrespondingFragment() {
        Assert.Equal(
            ArtifactSpec.Name.TachyonStoneFragment,
            ArtifactSpec.Name.TachyonStone.CorrespondingFragment());
    }

    [Fact]
    public void CorrespondingFragment_NonStone() {
        Assert.Equal(
            ArtifactSpec.Name.Unknown,
            ArtifactSpec.Name.TachyonStoneFragment.CorrespondingFragment());
    }
}
