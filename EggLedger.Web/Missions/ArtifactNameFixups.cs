namespace EggLedger.Web.Missions;

public static class ArtifactNameFixups {
    public static string ApplyDisplayNameOverrides(string protoName) =>
        protoName
            .Replace("ORNATE_GUSSET", "GUSSET", StringComparison.Ordinal)
            .Replace("VIAL_MARTIAN_DUST", "VIAL_OF_MARTIAN_DUST", StringComparison.Ordinal);
}
