using EggLedger.Web.Missions;

namespace EggLedger.Web.Tests.Missions;

public sealed class LifetimeSorterTests {
    private static DropLike D(int id, int level, int rarity, int count, double quality = 0, int iv = 0, string spec = "Artifact") =>
        new() { Id = id, Name = "X", Level = level, Rarity = rarity, Count = count, Quality = quality, IvOrder = iv, SpecType = spec };

    [Theory]
    [InlineData("default", LifetimeSortMethod.Default)]
    [InlineData("iv", LifetimeSortMethod.Iv)]
    [InlineData("count", LifetimeSortMethod.Count)]
    [InlineData("random", LifetimeSortMethod.Random)]
    [InlineData("nonsense", LifetimeSortMethod.Default)]
    [InlineData(null, LifetimeSortMethod.Default)]
    public void ParseMethod_MatchesVueSwitch(string? value, LifetimeSortMethod expected) =>
        Assert.Equal(expected, LifetimeSorter.ParseMethod(value));

    [Theory]
    [InlineData(LifetimeSortMethod.Default, "default")]
    [InlineData(LifetimeSortMethod.Iv, "iv")]
    [InlineData(LifetimeSortMethod.Count, "count")]
    [InlineData(LifetimeSortMethod.Random, "random")]
    public void MethodString_RoundTrips(LifetimeSortMethod method, string expected) =>
        Assert.Equal(expected, LifetimeSorter.MethodString(method));

    [Fact]
    public void SortGroupByCount_OrdersByCountDesc() {
        var input = new[] {
            D(1, 0, 0, count: 1),
            D(2, 0, 0, count: 5),
            D(3, 0, 0, count: 3),
        };

        var sorted = LifetimeSorter.SortGroupByCount(input);

        Assert.Equal(5, sorted[0].Count);
        Assert.Equal(3, sorted[1].Count);
        Assert.Equal(1, sorted[2].Count);
    }

    [Fact]
    public void SortGroupByCount_TieBreaksByLevelThenRarityThenIdThenQuality() {

        var input = new[] {
            D(1, level: 1, rarity: 0, count: 2, quality: 0),
            D(2, level: 2, rarity: 0, count: 2, quality: 0),
            D(3, level: 2, rarity: 1, count: 2, quality: 0),
            D(4, level: 2, rarity: 1, count: 2, quality: 9),
            D(5, level: 2, rarity: 1, count: 2, quality: 9),
        };

        var sorted = LifetimeSorter.SortGroupByCount(input);

        Assert.Equal(2, sorted[0].Level);
        Assert.Equal(1, sorted[0].Rarity);

        Assert.Equal(5, sorted[0].Id);
        Assert.Equal(4, sorted[1].Id);

        Assert.Equal(3, sorted[2].Id);
        Assert.Equal(2, sorted[3].Id);
        Assert.Equal(1, sorted[4].Id);
    }

    [Fact]
    public void Sort_Default_DelegatesToSortGroupAlreadyCombed() {
        var data = new LifetimeData {
            Artifacts = [D(1, 2, 0, 1), D(2, 0, 0, 1)],
        };

        LifetimeSorter.Sort(data, LifetimeSortMethod.Default);

        Assert.Equal(0, data.Artifacts[0].Level);
        Assert.Equal(2, data.Artifacts[1].Level);
    }

    [Fact]
    public void Sort_Count_AppliesToEveryBucket() {
        var data = new LifetimeData {
            Artifacts = [D(1, 0, 0, 1), D(2, 0, 0, 9)],
            Stones = [D(3, 0, 0, 1), D(4, 0, 0, 9)],
            StoneFragments = [D(5, 0, 0, 1), D(6, 0, 0, 9)],
            Ingredients = [D(7, 0, 0, 1), D(8, 0, 0, 9)],
        };

        LifetimeSorter.Sort(data, LifetimeSortMethod.Count);

        Assert.Equal(9, data.Artifacts[0].Count);
        Assert.Equal(9, data.Stones[0].Count);
        Assert.Equal(9, data.StoneFragments[0].Count);
        Assert.Equal(9, data.Ingredients[0].Count);
    }

    [Fact]
    public void Sort_Random_PreservesAllElements() {
        var data = new LifetimeData {
            Artifacts = [D(1, 0, 0, 1), D(2, 0, 0, 1), D(3, 0, 0, 1)],
        };

        LifetimeSorter.Sort(data, LifetimeSortMethod.Random, new Random(42));

        Assert.Equal(3, data.Artifacts.Count);
        var ids = data.Artifacts.Select(d => d.Id).OrderBy(x => x).ToArray();
        Assert.Equal([1, 2, 3], ids);
    }
}
