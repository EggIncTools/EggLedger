using EggLedger.Domain.MissionQuery;
using EggLedger.Web.Missions;

namespace EggLedger.Web.Tests.Missions;

public sealed class LifetimeAggregatorTests {
    private static MissionDrop Drop(
        int id,
        string spec,
        int level = 0,
        int rarity = 0,
        string name = "X",
        double quality = 0,
        int iv = 0,
        string effect = "") =>
        new() {
            Id = id,
            SpecType = spec,
            Name = name,
            GameName = name,
            EffectString = effect,
            Level = level,
            Rarity = rarity,
            Quality = quality,
            IVOrder = iv,
        };

    private static Dictionary<string, List<MissionDrop>> Missions(params (string Id, MissionDrop[] Drops)[] missions) =>
        missions.GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.Last().Drops.ToList());

    [Fact]
    public void Aggregate_RepresentativeInput_ProducesNonEmptyGroups() {

        var input = Missions(
            ("m1", new[] {
                Drop(1, "Artifact", level: 2, rarity: 3, name: "TACHYON_DEFLECTOR"),
                Drop(2, "Stone", name: "TACHYON_STONE"),
                Drop(3, "StoneFragment", name: "TACHYON_STONE_FRAGMENT"),
                Drop(4, "Ingredient", name: "GOLD_METEORITE"),
            }),
            ("m2", new[] {
                Drop(1, "Artifact", level: 2, rarity: 3, name: "TACHYON_DEFLECTOR"),
            }));

        var result = LifetimeAggregator.Aggregate(input);

        Assert.NotEmpty(result.Artifacts);
        Assert.NotEmpty(result.Stones);
        Assert.NotEmpty(result.StoneFragments);
        Assert.NotEmpty(result.Ingredients);
        Assert.Equal(2, result.MissionCount);
    }

    [Fact]
    public void Aggregate_CombinesIdenticalDropsAndSumsCount() {

        var input = Missions(
            ("m1", new[] { Drop(1, "Artifact", level: 5, rarity: 2) }),
            ("m2", new[] { Drop(1, "Artifact", level: 5, rarity: 2) }));

        var result = LifetimeAggregator.Aggregate(input);

        Assert.Single(result.Artifacts);
        Assert.Equal(2, result.Artifacts[0].Count);
    }

    [Fact]
    public void Aggregate_MergeKeyIsIdLevelRarity_NotName() {

        var input = Missions(
            ("m1", new[] {
                Drop(1, "Artifact", level: 1, rarity: 0),
                Drop(1, "Artifact", level: 2, rarity: 0),
            }));

        var result = LifetimeAggregator.Aggregate(input);

        Assert.Equal(2, result.Artifacts.Count);
        Assert.Equal(1, result.Artifacts[0].Count);
        Assert.Equal(1, result.Artifacts[1].Count);
    }

    [Fact]
    public void Aggregate_DifferentRarity_AreSeparateGroups() {
        var input = Missions(
            ("m1", new[] {
                Drop(1, "Artifact", level: 0, rarity: 0),
                Drop(1, "Artifact", level: 0, rarity: 1),
            }));

        var result = LifetimeAggregator.Aggregate(input);

        Assert.Equal(2, result.Artifacts.Count);
    }

    [Fact]
    public void Aggregate_RoutesEachSpecTypeToItsBucket() {
        var input = Missions(
            ("m1", new[] {
                Drop(1, "Artifact"),
                Drop(2, "Stone"),
                Drop(3, "StoneFragment"),
                Drop(4, "Ingredient"),
            }));

        var result = LifetimeAggregator.Aggregate(input);

        Assert.Single(result.Artifacts);
        Assert.Single(result.Stones);
        Assert.Single(result.StoneFragments);
        Assert.Single(result.Ingredients);
    }

    [Fact]
    public void Aggregate_UnknownSpecType_IsDropped() {

        var input = Missions(("m1", new[] { Drop(1, "Mystery") }));

        var result = LifetimeAggregator.Aggregate(input);

        Assert.Empty(result.Artifacts);
        Assert.Empty(result.Stones);
        Assert.Empty(result.StoneFragments);
        Assert.Empty(result.Ingredients);
    }

    [Fact]
    public void Aggregate_FirstOccurrenceIsRepresentative_PreservesOrder() {
        var input = Missions(
            ("m1", new[] {
                Drop(5, "Artifact", level: 0, name: "FIRST"),
                Drop(6, "Artifact", level: 0, name: "SECOND"),
            }));

        var result = LifetimeAggregator.Aggregate(input);

        Assert.Equal("FIRST", result.Artifacts[0].Name);
        Assert.Equal("SECOND", result.Artifacts[1].Name);
    }

    [Fact]
    public void Aggregate_PreservesDisplayFields() {
        var input = Missions(
            ("m1", new[] { Drop(9, "Artifact", level: 3, rarity: 2, name: "QUANTUM", quality: 4.5, iv: 7, effect: "+75% egg value") }));

        var result = LifetimeAggregator.Aggregate(input);

        var d = result.Artifacts[0];
        Assert.Equal(9, d.Id);
        Assert.Equal("QUANTUM", d.Name);
        Assert.Equal(3, d.Level);
        Assert.Equal(2, d.Rarity);
        Assert.Equal(4.5, d.Quality);
        Assert.Equal(7, d.IvOrder);
        Assert.Equal("Artifact", d.SpecType);

        Assert.Equal("QUANTUM", d.GameName);
        Assert.Equal("+75% egg value", d.EffectString);
    }

    [Fact]
    public void MergeDropArrays_SumsCountsAndCarriesFields() {
        static DropLike Item(int id, int level, int rarity, int count, string name, string effect) => new() {
            Id = id,
            Level = level,
            Rarity = rarity,
            Count = count,
            Name = name,
            GameName = name,
            EffectString = effect,
            SpecType = "Artifact",
        };
        var a = new List<DropLike> { Item(1, 2, 3, 1, "DEFLECTOR", "+10%") };
        var b = new List<DropLike> { Item(1, 2, 3, 4, "DEFLECTOR", "+10%"), Item(2, 0, 0, 2, "OTHER", "x") };

        var merged = LifetimeAggregator.MergeDropArrays(new IReadOnlyList<DropLike>[] { a, b });

        Assert.Equal(2, merged.Count);
        var d = merged[0];
        Assert.Equal(1, d.Id);
        Assert.Equal(5, d.Count);
        Assert.Equal("DEFLECTOR", d.GameName);
        Assert.Equal("+10%", d.EffectString);
    }

    [Fact]
    public void MergeDropArrays_DifferentRarity_StaysSeparate() {
        static DropLike Item(int rarity, int count) => new() {
            Id = 1,
            Level = 2,
            Rarity = rarity,
            Count = count,
            Name = "X",
            SpecType = "Artifact",
        };
        var merged = LifetimeAggregator.MergeDropArrays(new IReadOnlyList<DropLike>[] {
            new List<DropLike> { Item(0, 1), Item(3, 1) },
        });

        Assert.Equal(2, merged.Count);
    }

    [Fact]
    public void Aggregate_EmptyInput_GivesEmptyGroupsAndZeroCount() {
        var result = LifetimeAggregator.Aggregate(new Dictionary<string, List<MissionDrop>>());

        Assert.Empty(result.Artifacts);
        Assert.Equal(0, result.MissionCount);
    }
}
