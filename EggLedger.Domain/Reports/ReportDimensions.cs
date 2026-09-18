namespace EggLedger.Domain.Reports;

public enum DimensionScope {
    Mission,
    Artifact,
}

public sealed record ReportDimension(string Value, string Label, DimensionScope Scope);

public static class ReportDimensions {
    public static readonly IReadOnlyList<ReportDimension> Mission = new[] {
        new ReportDimension("ship_type", "Ship Type", DimensionScope.Mission),
        new ReportDimension("duration_type", "Duration Type", DimensionScope.Mission),
        new ReportDimension("level", "Level", DimensionScope.Mission),
        new ReportDimension("mission_type", "Mission Type", DimensionScope.Mission),
        new ReportDimension("mission_target", "Mission Target", DimensionScope.Mission),
    };

    public static readonly IReadOnlyList<ReportDimension> Artifact = new[] {
        new ReportDimension("artifact_name", "Artifact Name", DimensionScope.Artifact),
        new ReportDimension("rarity", "Rarity", DimensionScope.Artifact),
        new ReportDimension("tier", "Tier", DimensionScope.Artifact),
        new ReportDimension("spec_type", "Spec Type", DimensionScope.Artifact),
    };

    public static readonly IReadOnlyList<ReportDimension> All =
        Mission.Concat(Artifact).ToList();

    public static readonly IReadOnlySet<string> ArtifactKeys =
        Artifact.Select(d => d.Value).ToHashSet(StringComparer.Ordinal);

    public static string DimensionLabel(string value) =>
        All.FirstOrDefault(d => d.Value == value)?.Label ?? value;
}
