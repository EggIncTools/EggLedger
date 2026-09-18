using System.Text.Json;
using System.Text.Json.Serialization;

namespace EggLedger.Domain.Eiafx;

public sealed record FamilyMeta(string Id, string Name);

public static class EiafxData {
    private const string ResourceName = "EggLedger.Domain.Resources.eiafx-data-min.json";

    private static readonly Lazy<ParsedData> _data = new(LoadEmbedded);

    public static IReadOnlyDictionary<(int AfxId, int AfxLevel), double> CraftingWeights => _data.Value.CraftingWeights;

    public static IReadOnlyDictionary<string, IReadOnlyList<int>> FamilyAfxIds => _data.Value.FamilyAfxIds;

    public static IReadOnlyList<FamilyMeta> Families => _data.Value.Families;

    public static double CraftingWeightOrOne(long afxId, long afxLevel) {
        if (CraftingWeights.TryGetValue(((int)afxId, (int)afxLevel), out var w) && w != 0d) {
            return w;
        }
        return 1d;
    }

    private sealed record ParsedData(
        IReadOnlyDictionary<(int, int), double> CraftingWeights,
        IReadOnlyDictionary<string, IReadOnlyList<int>> FamilyAfxIds,
        IReadOnlyList<FamilyMeta> Families);

    private static ParsedData LoadEmbedded() {
        var asm = typeof(EiafxData).Assembly;
        using var stream = asm.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"embedded eiafx data resource not found: {ResourceName}");
        var cfg = JsonSerializer.Deserialize(stream, EiafxDataJsonContext.Default.ArtifactDataConfig)
            ?? throw new InvalidOperationException("eiafx data json deserialized to null");
        return Parse(cfg);
    }


    private static ParsedData Parse(ArtifactDataConfig cfg) {
        var families = cfg.Families ?? [];

        var tierMap = new Dictionary<(int, int), TierData>();
        foreach (var fam in families) {
            foreach (var t in fam.Tiers ?? []) {
                tierMap[(t.AfxId, t.AfxLevel)] = t;
            }
        }

        var memo = new Dictionary<(int, int), double>();
        double ComputeWeight(int afxId, int afxLevel) {
            var key = (afxId, afxLevel);
            if (memo.TryGetValue(key, out var cached)) {
                return cached;
            }
            if (!tierMap.TryGetValue(key, out var t) || t.Recipe == null || t.Recipe.Count == 0) {
                memo[key] = 1.0;
                return 1.0;
            }

            memo[key] = 0;
            var w = 0.0;
            foreach (var ing in t.Recipe) {
                w += ing.Count * ComputeWeight(ing.AfxId, ing.AfxLevel);
            }
            memo[key] = w;
            return w;
        }

        foreach (var key in tierMap.Keys) {
            ComputeWeight(key.Item1, key.Item2);
        }

        var fids = families
            .GroupBy(f => f.Id)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<int>)(g.Last().ChildAfxIds ?? []));

        var fams = families.ConvertAll(f => new FamilyMeta(f.Id, f.Name));

        return new ParsedData(memo, fids, fams);
    }

    internal sealed class ArtifactDataConfig {
        [JsonPropertyName("families")]
        public List<FamilyData>? Families { get; set; }
    }

    internal sealed class FamilyData {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";
        [JsonPropertyName("child_afx_ids")]
        public List<int>? ChildAfxIds { get; set; }
        [JsonPropertyName("tiers")]
        public List<TierData>? Tiers { get; set; }
    }

    internal sealed class TierData {
        [JsonPropertyName("afx_id")]
        public int AfxId { get; set; }
        [JsonPropertyName("afx_level")]
        public int AfxLevel { get; set; }
        [JsonPropertyName("recipe")]
        public List<IngredientData>? Recipe { get; set; }
    }

    internal sealed class IngredientData {
        [JsonPropertyName("afx_id")]
        public int AfxId { get; set; }
        [JsonPropertyName("afx_level")]
        public int AfxLevel { get; set; }
        [JsonPropertyName("count")]
        public int Count { get; set; }
    }
}

[JsonSerializable(typeof(EiafxData.ArtifactDataConfig))]
internal sealed partial class EiafxDataJsonContext : JsonSerializerContext;
