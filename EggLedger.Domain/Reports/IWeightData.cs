namespace EggLedger.Domain.Reports;

public interface IWeightData {
    double CraftingWeight(long artifactId, long level);

    IReadOnlyList<int> FamilyAfxIds(string familyId);
}
