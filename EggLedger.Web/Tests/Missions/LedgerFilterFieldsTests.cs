using EggLedger.Domain.Reports;
using EggLedger.Web.Missions;

namespace EggLedger.Web.Tests.Missions;

public sealed class LedgerFilterFieldsTests {
    [Fact]
    public void ReportFilterFields_HasExpectedKeysInOrder() {
        var keys = LedgerFilterFields.ReportFilterFields.Select(f => f.Key).ToArray();
        Assert.Equal(new[] {
            "ship", "duration", "level", "target", "type", "launchDT", "returnDT",
            "dubcap", "buggedcap", "drops",
            "artifact_name", "artifact_rarity", "artifact_tier", "artifact_spec_type", "artifact_quality",
        }, keys);
    }

    [Fact]
    public void Find_ResolvesKeysAndTheFarmAlias() {
        Assert.Equal("Ship", LedgerFilters.Find("ship")!.Label);
        Assert.Equal("type", LedgerFilters.Find("farm")!.Key);
        Assert.Null(LedgerFilters.Find("nope"));
    }

    [Fact]
    public void MissionBarOpsFor_DateFields_UseDayEqOnOperator() {
        var ops = LedgerFilterFields.MissionBarOpsFor(LedgerFilters.Find("launchDT")!);
        Assert.Equal(new[] { "d=", "<", ">" }, ops.Select(o => o.Value).ToArray());

        var shipOps = LedgerFilterFields.MissionBarOpsFor(LedgerFilters.Find("ship")!);
        Assert.Same(LedgerFilters.Find("ship")!.Ops, shipOps);
    }

    [Fact]
    public void ReportOpsFor_DateFields_UseFullDateSet() {
        var ops = LedgerFilterFields.ReportOpsFor(LedgerFilters.Find("launchDT")!);
        Assert.Equal(new[] { "=", "<", ">", "<=", ">=", FilterOps.Within }, ops.Select(o => o.Value).ToArray());
    }

    [Fact]
    public void Scopes_PartitionMissionAndArtifact() {
        Assert.Equal(10, LedgerFilterFields.ReportMissionFields.Count);
        Assert.Equal(5, LedgerFilterFields.ReportFilterFields.Count(f => f.Scope == LedgerFilters.ArtifactScope));
    }
}
