using EggLedger.Domain.Reports;

namespace EggLedger.Domain.Tests.Reports;


public class ReportDimensionsTests {
    [Fact]
    public void Mission_HasExpectedValuesAndLabels() {
        var expected = new (string Value, string Label)[] {
            ("ship_type", "Ship Type"),
            ("duration_type", "Duration Type"),
            ("level", "Level"),
            ("mission_type", "Mission Type"),
            ("mission_target", "Mission Target"),
        };
        Assert.Equal(expected.Length, ReportDimensions.Mission.Count);
        for (int i = 0; i < expected.Length; i++) {
            Assert.Equal(expected[i].Value, ReportDimensions.Mission[i].Value);
            Assert.Equal(expected[i].Label, ReportDimensions.Mission[i].Label);
            Assert.Equal(DimensionScope.Mission, ReportDimensions.Mission[i].Scope);
        }
    }

    [Fact]
    public void Artifact_HasExpectedValuesAndLabels() {
        var expected = new (string Value, string Label)[] {
            ("artifact_name", "Artifact Name"),
            ("rarity", "Rarity"),
            ("tier", "Tier"),
            ("spec_type", "Spec Type"),
        };
        Assert.Equal(expected.Length, ReportDimensions.Artifact.Count);
        for (int i = 0; i < expected.Length; i++) {
            Assert.Equal(expected[i].Value, ReportDimensions.Artifact[i].Value);
            Assert.Equal(expected[i].Label, ReportDimensions.Artifact[i].Label);
            Assert.Equal(DimensionScope.Artifact, ReportDimensions.Artifact[i].Scope);
        }
    }

    [Fact]
    public void All_IsMissionThenArtifact() {
        Assert.Equal(9, ReportDimensions.All.Count);
        Assert.Equal("ship_type", ReportDimensions.All[0].Value);
        Assert.Equal("artifact_name", ReportDimensions.All[5].Value);
    }

    [Fact]
    public void ArtifactKeys_ContainsOnlyArtifactValues() {
        Assert.Contains("rarity", ReportDimensions.ArtifactKeys);
        Assert.DoesNotContain("ship_type", ReportDimensions.ArtifactKeys);
        Assert.Equal(4, ReportDimensions.ArtifactKeys.Count);
    }

    [Theory]
    [InlineData("ship_type", "Ship Type")]
    [InlineData("rarity", "Rarity")]
    [InlineData("unknown_key", "unknown_key")]
    public void DimensionLabel_FallsBackToValue(string value, string expected) =>
        Assert.Equal(expected, ReportDimensions.DimensionLabel(value));
}
