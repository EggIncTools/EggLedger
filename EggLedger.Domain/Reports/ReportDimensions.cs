using System.Collections.Frozen;

namespace EggLedger.Domain.Reports;

public enum DimensionScope {
    Mission,
    Artifact,
}

public sealed record ReportDimension(string Value, string Label, DimensionScope Scope);

public static class ReportDimensions {
    public static IReadOnlyList<ReportDimension> Mission { get; } = [
        new ReportDimension("ship_type", "Ship Type", DimensionScope.Mission),
        new ReportDimension("duration_type", "Duration Type", DimensionScope.Mission),
        new ReportDimension("level", "Level", DimensionScope.Mission),
        new ReportDimension("mission_type", "Mission Type", DimensionScope.Mission),
        new ReportDimension("mission_target", "Mission Target", DimensionScope.Mission),
    ];

    public static IReadOnlyList<ReportDimension> Artifact { get; } = [
        new ReportDimension("artifact_name", "Artifact Name", DimensionScope.Artifact),
        new ReportDimension("rarity", "Rarity", DimensionScope.Artifact),
        new ReportDimension("tier", "Tier", DimensionScope.Artifact),
        new ReportDimension("spec_type", "Spec Type", DimensionScope.Artifact),
    ];

    public static IReadOnlyList<ReportDimension> All { get; } = [.. Mission, .. Artifact];

    public static IReadOnlySet<string> ArtifactKeys { get; } =
        Artifact.Select(d => d.Value).ToFrozenSet(StringComparer.Ordinal);

    public static string DimensionLabel(string value) =>
        All.FirstOrDefault(d => d.Value == value)?.Label ?? value;
}
