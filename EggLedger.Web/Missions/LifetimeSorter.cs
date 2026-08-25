namespace EggLedger.Web.Missions;

public enum LifetimeSortMethod {
    Default,
    Iv,
    Count,
    Random,
}

public static class LifetimeSorter {
    public static LifetimeSortMethod ParseMethod(string? value) => value switch {
        "iv" => LifetimeSortMethod.Iv,
        "count" => LifetimeSortMethod.Count,
        "random" => LifetimeSortMethod.Random,
        _ => LifetimeSortMethod.Default,
    };

    public static string MethodString(LifetimeSortMethod method) => method switch {
        LifetimeSortMethod.Iv => "iv",
        LifetimeSortMethod.Count => "count",
        LifetimeSortMethod.Random => "random",
        _ => "default",
    };

    public static void Sort(LifetimeData data, LifetimeSortMethod method, Random? rng = null) {
        data.Artifacts = SortList(data.Artifacts, method, rng);
        data.Stones = SortList(data.Stones, method, rng);
        data.StoneFragments = SortList(data.StoneFragments, method, rng);
        data.Ingredients = SortList(data.Ingredients, method, rng);
    }

    private static List<DropLike> SortList(IReadOnlyList<DropLike> list, LifetimeSortMethod method, Random? rng) => method switch {
        LifetimeSortMethod.Iv => DropSorter.InventoryVisualizerSort(list),
        LifetimeSortMethod.Count => SortGroupByCount(list),
        LifetimeSortMethod.Random => Shuffle(list, rng ?? Random.Shared),
        _ => DropSorter.SortGroupAlreadyCombed(list),
    };

    public static List<DropLike> SortGroupByCount(IEnumerable<DropLike> collection) =>
        DropSorter.StableSort(collection, CountComparer);

    private static int CountComparer(DropLike a, DropLike b) {
        if (a.Count != b.Count) {
            return a.Count > b.Count ? -1 : 1;
        }
        if (a.Level != b.Level) {
            return a.Level > b.Level ? -1 : 1;
        }
        if (a.Rarity != b.Rarity) {
            return a.Rarity > b.Rarity ? -1 : 1;
        }
        if (a.Id != b.Id) {
            return a.Id > b.Id ? -1 : 1;
        }
        if (a.Quality != b.Quality) {
            return a.Quality < b.Quality ? -1 : 1;
        }
        return 0;
    }

    public static List<DropLike> Shuffle(IEnumerable<DropLike> collection, Random rng) {
        var list = new List<DropLike>(collection);
        for (int i = list.Count - 1; i > 0; i--) {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
        return list;
    }
}
