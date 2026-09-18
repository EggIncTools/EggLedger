using EggLedger.Domain.MissionQuery;

namespace EggLedger.Web.Missions;

public sealed class LifetimeData {
    public int MissionCount { get; set; }
    public List<DropLike> Artifacts { get; set; } = [];
    public List<DropLike> Stones { get; set; } = [];
    public List<DropLike> StoneFragments { get; set; } = [];
    public List<DropLike> Ingredients { get; set; } = [];
}

public static class LifetimeAggregator {
    public static LifetimeData Aggregate(IReadOnlyDictionary<string, List<MissionDrop>> dropsByMission) {
        var artifacts = new Bucket();
        var stones = new Bucket();
        var stoneFragments = new Bucket();
        var ingredients = new Bucket();

        foreach (var kvp in dropsByMission) {
            foreach (var drop in kvp.Value) {
                var bucket = drop.SpecType switch {
                    "Artifact" => artifacts,
                    "Stone" => stones,
                    "StoneFragment" => stoneFragments,
                    "Ingredient" => ingredients,
                    _ => null,
                };
                bucket?.Merge(drop);
            }
        }

        return new LifetimeData {
            MissionCount = dropsByMission.Count,
            Artifacts = artifacts.Items,
            Stones = stones.Items,
            StoneFragments = stoneFragments.Items,
            Ingredients = ingredients.Items,
        };
    }


    public static List<DropLike> MergeDropArrays(IEnumerable<IReadOnlyList<DropLike>> arrays) {
        var map = new Dictionary<string, DropLike>();
        var order = new List<string>();
        foreach (var arr in arrays) {
            foreach (var item in arr) {
                string key = item.Id + "_" + item.Level + "_" + item.Rarity;
                if (map.TryGetValue(key, out var existing)) {
                    existing.Count += item.Count;
                } else {
                    map[key] = new DropLike {
                        Id = item.Id,
                        Name = item.Name,
                        GameName = item.GameName,
                        EffectString = item.EffectString,
                        Level = item.Level,
                        Rarity = item.Rarity,
                        Quality = item.Quality,
                        IvOrder = item.IvOrder,
                        SpecType = item.SpecType,
                        Count = item.Count,
                    };
                    order.Add(key);
                }
            }
        }
        return [.. order.Select(key => map[key])];
    }


    private sealed class Bucket {
        public List<DropLike> Items { get; } = [];
        private readonly Dictionary<string, DropLike> _index = [];

        public void Merge(MissionDrop drop) {
            string key = drop.Id + "_" + drop.Level + "_" + drop.Rarity;
            if (_index.TryGetValue(key, out var existing)) {
                existing.Count += 1;
                return;
            }
            var rep = new DropLike {
                Id = drop.Id,
                Name = drop.Name,
                GameName = drop.GameName,
                EffectString = drop.EffectString,
                Level = drop.Level,
                Rarity = drop.Rarity,
                Quality = drop.Quality,
                IvOrder = drop.IVOrder,
                SpecType = drop.SpecType,
                Count = 1,
            };
            _index[key] = rep;
            Items.Add(rep);
        }
    }
}
