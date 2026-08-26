using EggLedger.Domain.Eiafx;

namespace EggLedger.Domain.Reports;

public sealed class EiafxWeightData : IWeightData {
    public static readonly EiafxWeightData Instance = new();

    public double CraftingWeight(long artifactId, long level) =>
        EiafxData.CraftingWeightOrOne(artifactId, level);

    public IReadOnlyList<int> FamilyAfxIds(string familyId) =>
        EiafxData.FamilyAfxIds.TryGetValue(familyId, out var ids) ? ids : [];
}
