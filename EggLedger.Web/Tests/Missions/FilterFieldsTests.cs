using EggLedger.Web.Missions;

namespace EggLedger.Web.Tests.Missions;

public sealed class FilterFieldsTests {
    [Fact]
    public void ReportFilterFields_HasExpectedKeysInOrder() {
        var keys = FilterFields.ReportFilterFields.Select(f => f.Key).ToArray();
        Assert.Equal(new[] {
            "ship", "duration", "level", "target", "type", "launchDT", "returnDT",
            "dubcap", "buggedcap", "drops",
            "artifact_name", "artifact_rarity", "artifact_tier", "artifact_spec_type", "artifact_quality",
        }, keys);
    }

    [Fact]
    public void GetReportField_FindsByKey() {
        Assert.Equal("Ship", FilterFields.GetReportField("ship")!.Label);
        Assert.Null(FilterFields.GetReportField("nope"));
    }

    [Fact]
    public void MissionBarOpsFor_DateFields_UseDayEqOnOperator() {
        var ops = FilterFields.MissionBarOpsFor(FilterFields.GetReportField("launchDT")!);
        Assert.Equal(new[] { "d=", "<", ">" }, ops.Select(o => o.Value).ToArray());
        Assert.Equal("d=", ops.First(o => o.Label == "on").Value);

        var shipOps = FilterFields.MissionBarOpsFor(FilterFields.GetReportField("ship")!);
        Assert.Same(FilterFields.GetReportField("ship")!.Ops, shipOps);
    }

    [Fact]
    public void Scopes_PartitionMissionAndArtifact() {
        Assert.Equal(10, FilterFields.ReportMissionFields().Count);
        Assert.Equal(5, FilterFields.ReportArtifactFields().Count);
    }

    [Fact]
    public void OperatorLists_MatchVue() {
        Assert.Equal(6, FilterFields.ComparisonOps.Count);
        Assert.Equal(2, FilterFields.EqualityOps.Count);
        Assert.Equal(5, FilterFields.DateOps.Count);
        Assert.Equal(2, FilterFields.DropsOps.Count);
    }
}
