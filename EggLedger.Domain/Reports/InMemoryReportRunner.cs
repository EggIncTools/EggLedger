using System.Globalization;

namespace EggLedger.Domain.Reports;

public sealed record MissionRowData {
    public string PlayerId { get; init; } = "";
    public string MissionId { get; init; } = "";
    public long Ship { get; init; }
    public long DurationType { get; init; }
    public long Level { get; init; }
    public long Target { get; init; }
    public long MissionType { get; init; }
    public long StartTimestamp { get; init; }
    public long ReturnTimestamp { get; init; }
    public long Capacity { get; init; }
    public long NominalCapacity { get; init; }
    public bool IsDubCap { get; init; }
    public bool IsBuggedCap { get; init; }
}

public sealed record ArtifactDropRowData {
    public string PlayerId { get; init; } = "";
    public string MissionId { get; init; } = "";
    public long DropIndex { get; init; }
    public long ArtifactId { get; init; }
    public string SpecType { get; init; } = "";
    public long Level { get; init; }
    public long Rarity { get; init; }
    public double Quality { get; init; }
}

public sealed record FuelRowData {
    public string PlayerId { get; init; } = "";
    public string MissionId { get; init; } = "";
    public long EggId { get; init; }
    public double Amount { get; init; }
}

public sealed class InMemoryReportRunner(IWeightData weights) {
    private readonly IWeightData _weights = weights ?? throw new ArgumentNullException(nameof(weights));

    public ReportResult Run(
        ReportDefinition def,
        IReadOnlyList<MissionRowData> missions,
        IReadOnlyList<ArtifactDropRowData> drops,
        IReadOnlyList<FuelRowData> fuel) {
        var db = new InMemoryMissionDb(def, missions, drops, fuel, _weights);
        var executor = new ReportExecutor(db, _weights);
        return executor.ExecuteReport(def);
    }
}
