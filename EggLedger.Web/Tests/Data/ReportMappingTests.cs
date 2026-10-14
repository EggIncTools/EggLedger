using EggIdentity.Reports;
using EggLedger.Domain.Reports;
using EggLedger.Web.Data;

namespace EggLedger.Web.Tests.Data;

public sealed class ReportMappingTests {
    private static ReportRow LegacyRow() => new() {
        Id = "r1",
        AccountId = "EI1",
        Name = "Report",
        Subject = "ships",
        Mode = "aggregate",
        DisplayMode = "bar",
        GroupBy = "ship_type",
        SecondaryGroupBy = "duration_type",
        Filters = "{\"and\":[{\"topLevel\":\"level\",\"op\":\"=\",\"val\":\"5\"}],\"or\":[]}",
        GridX = 1,
        GridY = 2,
        GridW = 3,
        GridH = 4,
        Color = "#abcdef",
        Description = "desc",
        ChartType = "line",
        SortOrder = 7,
        CreatedAt = 100,
        UpdatedAt = 200,
        GroupId = "g1",
        NormalizeBy = "row_pct",
    };

    [Fact]
    public void ToReport_UpgradesLegacyColumns() {
        var report = ReportMapping.ToReport(LegacyRow());

        Assert.Equal("Report", report.Definition.Title);
        Assert.Equal(LedgerReportKeys.Missions, report.Definition.Query.Source);
        Assert.Equal(LedgerReportKeys.Ship, report.Definition.Query.GroupBy[0].Dimension);
        Assert.Equal(LedgerReportKeys.Duration, report.Definition.Query.GroupBy[1].Dimension);
        Assert.Equal(NormalizeKind.Row, report.Definition.Query.Normalize.Kind);
        Assert.Equal("level", report.Definition.Query.Filters.And[0].Field);
        Assert.Equal("EI1", report.AccountId);
        Assert.Equal(new ReportPlacement("r1", 1, 2, 3, 4, 7) { Group = "g1" }, report.Placement);
    }

    [Fact]
    public void ToRow_ThenToReport_RoundTripsThroughDefinitionColumn() {
        var report = ReportMapping.ToReport(LegacyRow());
        var row = ReportMapping.ToRow(report);

        Assert.NotEmpty(row.Definition);
        var back = ReportMapping.ToReport(row);
        Assert.Equal(report.Definition.Title, back.Definition.Title);
        Assert.Equal(report.Definition.Query.GroupBy.Count, back.Definition.Query.GroupBy.Count);
        Assert.Equal(report.Placement, back.Placement);
    }

    [Fact]
    public void ToReport_UnreadableDefinition_FallsBackToLegacyColumns() {
        var report = ReportMapping.ToReport(LegacyRow() with { Definition = "not json" });

        Assert.Equal("Report", report.Definition.Title);
    }

    [Fact]
    public void WithPlacement_WritesPlacementColumns() {
        var row = ReportMapping.WithPlacement(new ReportRow { Id = "r1" }, new ReportPlacement("r1", 2, 3, 4, 5, 6) { Group = "g" });

        Assert.Equal((2, 3, 4, 5, 6, "g"), (row.GridX, row.GridY, row.GridW, row.GridH, row.SortOrder, row.GroupId));
    }

    [Fact]
    public void ParseFilters_TolerantOfBlankAndInvalid() {
        Assert.Empty(ReportMapping.ParseFilters("").And);
        Assert.Empty(ReportMapping.ParseFilters("garbage").Or);
        Assert.Empty(ReportMapping.ParseFilters(null).And);
    }
}
