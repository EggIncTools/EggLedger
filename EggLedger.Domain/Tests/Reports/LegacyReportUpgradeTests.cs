using System.Text.Json;
using EggIdentity.Filters;
using EggIdentity.Reports;
using EggLedger.Domain.Reports;

namespace EggLedger.Domain.Tests.Reports;

public sealed class LegacyReportUpgradeTests {
    private static LegacyReportDefinition Base() => new() {
        Id = "r1",
        AccountId = "a1",
        Name = "My Report",
        Subject = "ships",
        Mode = "aggregate",
        DisplayMode = "",
        GroupBy = "ship_type",
        SecondaryGroupBy = "",
        TimeBucket = "",
        CustomBucketN = 0,
        CustomBucketUnit = "",
        Filters = FilterSet.Empty,
        GridX = 0,
        GridY = 0,
        GridW = 0,
        GridH = 0,
        Weight = "",
        Color = "",
        Description = "",
        ChartType = "",
        SortOrder = 0,
        CreatedAt = 0,
        UpdatedAt = 0,
        ValueFilterOp = "",
        ValueFilterThreshold = 0,
        GroupId = "",
        NormalizeBy = "",
        LabelColors = "",
        UnfilledColor = "",
        FamilyWeight = "",
        MennoEnabled = false,
        MennoCompareMode = "",
        MinSampleSize = 0,
    };

    [Fact]
    public void Convert_MapsBasicFields() {
        var legacy = Base() with { Id = "r1", Name = "Report One", Description = "" };
        var (definition, _) = LegacyReportUpgrade.Convert(legacy);
        Assert.Equal("r1", definition.Id);
        Assert.Equal("Report One", definition.Title);
        Assert.Null(definition.Description);
        Assert.Equal(ReportCodec.CurrentVersion, definition.Version);
    }

    [Theory]
    [InlineData("ships", LedgerReportKeys.Missions)]
    [InlineData("", LedgerReportKeys.Missions)]
    [InlineData("artifacts", LedgerReportKeys.Artifacts)]
    [InlineData("fuel_eggs", LedgerReportKeys.Fuel)]
    public void Convert_MapsSubjectToSource(string subject, string expectedSource) {
        var legacy = Base() with { Subject = subject };
        var (definition, _) = LegacyReportUpgrade.Convert(legacy);
        Assert.Equal(expectedSource, definition.Query.Source);
    }

    [Fact]
    public void Convert_FuelSubject_SetsFuelAmountSumMeasure() {
        var legacy = Base() with { Subject = "fuel_eggs" };
        var (definition, _) = LegacyReportUpgrade.Convert(legacy);
        Assert.Equal(1, definition.Query.Measures.Count);
        Assert.Equal(new MeasureRequest(LedgerReportKeys.FuelAmount, Aggregation.Sum), definition.Query.Measures[0]);
    }

    [Theory]
    [InlineData("ship_type", LedgerReportKeys.Ship)]
    [InlineData("duration_type", LedgerReportKeys.Duration)]
    [InlineData("level", LedgerReportKeys.Level)]
    [InlineData("mission_type", LedgerReportKeys.MissionType)]
    [InlineData("mission_target", LedgerReportKeys.Target)]
    [InlineData("artifact_name", LedgerReportKeys.ArtifactName)]
    [InlineData("rarity", LedgerReportKeys.Rarity)]
    [InlineData("tier", LedgerReportKeys.Tier)]
    [InlineData("spec_type", LedgerReportKeys.SpecType)]
    public void Convert_MapsGroupByNameToDimension(string legacyGroupBy, string expectedDimension) {
        var legacy = Base() with { GroupBy = legacyGroupBy };
        var (definition, _) = LegacyReportUpgrade.Convert(legacy);
        Assert.True(definition.Query.GroupBy.Count >= 1);
        Assert.Equal(expectedDimension, definition.Query.GroupBy[0].Dimension);
    }

    [Fact]
    public void Convert_AggregateWithoutSecondary_SortsByRowCountDescending() {
        var legacy = Base() with { Mode = "aggregate", GroupBy = "ship_type", SecondaryGroupBy = "" };
        var (definition, _) = LegacyReportUpgrade.Convert(legacy);
        Assert.Equal(1, definition.Query.Sort.Count);
        Assert.Equal(new SortKey(MeasureRequest.RowCountKey, true), definition.Query.Sort[0]);
    }

    [Fact]
    public void Convert_AggregateWithSecondary_HasTwoGroupKeysAndNoSort() {
        var legacy = Base() with { Mode = "aggregate", GroupBy = "ship_type", SecondaryGroupBy = "duration_type" };
        var (definition, _) = LegacyReportUpgrade.Convert(legacy);
        Assert.Equal(2, definition.Query.GroupBy.Count);
        Assert.Equal(LedgerReportKeys.Ship, definition.Query.GroupBy[0].Dimension);
        Assert.Equal(LedgerReportKeys.Duration, definition.Query.GroupBy[1].Dimension);
        Assert.Equal(0, definition.Query.Sort.Count);
    }

    [Fact]
    public void Convert_TimeSeriesMonth_GroupsByLaunchMonth() {
        var legacy = Base() with { Mode = "time_series", TimeBucket = "month", SecondaryGroupBy = "" };
        var (definition, _) = LegacyReportUpgrade.Convert(legacy);
        Assert.Equal(new GroupKey(LedgerReportKeys.Launched, TimeBucket.Month), definition.Query.GroupBy[0]);
    }

    [Fact]
    public void Convert_TimeSeriesMonthWithSecondary_SecondaryIsSecondGroupKey() {
        var legacy = Base() with { Mode = "time_series", TimeBucket = "month", SecondaryGroupBy = "ship_type" };
        var (definition, _) = LegacyReportUpgrade.Convert(legacy);
        Assert.Equal(2, definition.Query.GroupBy.Count);
        Assert.Equal(new GroupKey(LedgerReportKeys.Launched, TimeBucket.Month), definition.Query.GroupBy[0]);
        Assert.Equal(LedgerReportKeys.Ship, definition.Query.GroupBy[1].Dimension);
    }

    [Fact]
    public void Convert_CustomBucketWeek_SetsWeekBucketAndWithinFilter() {
        var legacy = Base() with { Mode = "time_series", TimeBucket = "custom", CustomBucketN = 3, CustomBucketUnit = "week" };
        var (definition, _) = LegacyReportUpgrade.Convert(legacy);
        Assert.Equal(TimeBucket.Week, definition.Query.GroupBy[0].Bucket);
        Assert.Contains(new FilterCondition(LedgerReportKeys.Launched, FilterOps.Within, "3w"), definition.Query.Filters.And);
    }

    [Theory]
    [InlineData("row_pct")]
    [InlineData("col_pct")]
    [InlineData("global_pct")]
    [InlineData("airtime")]
    [InlineData("launches")]
    [InlineData("none")]
    public void Convert_MapsNormalizeBy(string normalizeBy) {
        var legacy = Base() with { NormalizeBy = normalizeBy };
        var (definition, _) = LegacyReportUpgrade.Convert(legacy);
        var expected = normalizeBy switch {
            "row_pct" => Normalize.Row,
            "col_pct" => Normalize.Column,
            "global_pct" => Normalize.Global,
            "airtime" => Normalize.By(LedgerReportKeys.Airtime),
            "launches" => Normalize.By(LedgerReportKeys.Launches),
            _ => Normalize.None,
        };
        Assert.Equal(expected, definition.Query.Normalize);
    }

    [Fact]
    public void Convert_ValueFilter_MapsToHavingClause() {
        var legacy = Base() with { ValueFilterOp = ">", ValueFilterThreshold = 5 };
        var (definition, _) = LegacyReportUpgrade.Convert(legacy);
        Assert.Equal(1, definition.Query.Having.Count);
        Assert.Equal(new HavingClause(MeasureRequest.RowCountKey, ">", 5), definition.Query.Having[0]);
    }

    [Fact]
    public void Convert_FamilyWeight_SetsCraftingWeightMeasureAndFamilyFilter() {
        var legacy = Base() with { FamilyWeight = "x" };
        var (definition, _) = LegacyReportUpgrade.Convert(legacy);
        Assert.Equal(1, definition.Query.Measures.Count);
        Assert.Equal(new MeasureRequest(LedgerReportKeys.CraftingWeight, Aggregation.Sum), definition.Query.Measures[0]);
        Assert.Contains(new FilterCondition(LedgerReportKeys.Family, FilterOps.Eq, "x"), definition.Query.Filters.And);
    }

    [Theory]
    [InlineData(4, SampleRule.Dim)]
    [InlineData(0, SampleRule.Show)]
    public void Convert_MinSampleSize_MapsToMinSampleAndSampleRule(int minSampleSize, SampleRule expectedRule) {
        var legacy = Base() with { MinSampleSize = minSampleSize };
        var (definition, _) = LegacyReportUpgrade.Convert(legacy);
        Assert.Equal(minSampleSize == 0 ? null : minSampleSize, definition.Query.MinSample);
        Assert.Equal(expectedRule, definition.View.Sample);
    }

    [Fact]
    public void Convert_DisplayModeHeatmap_SetsViewChartToHeatmap() {
        var legacy = Base() with { DisplayMode = "heatmap", ChartType = "" };
        var (definition, _) = LegacyReportUpgrade.Convert(legacy);
        Assert.Equal("heatmap", definition.View.Chart);
    }

    [Theory]
    [InlineData("", "bar")]
    [InlineData("line", "line")]
    public void Convert_ChartType_DefaultsToBarWhenEmpty(string chartType, string expectedChart) {
        var legacy = Base() with { DisplayMode = "", ChartType = chartType };
        var (definition, _) = LegacyReportUpgrade.Convert(legacy);
        Assert.Equal(expectedChart, definition.View.Chart);
    }

    [Theory]
    [InlineData("#abcdef", "#abcdef")]
    [InlineData("red", null)]
    public void Convert_Color_KeepsValidHexOtherwiseNull(string color, string? expected) {
        var legacy = Base() with { Color = color };
        var (definition, _) = LegacyReportUpgrade.Convert(legacy);
        Assert.Equal(expected, definition.View.Color);
    }

    [Theory]
    [InlineData("#abcdef", "#abcdef")]
    [InlineData("red", null)]
    public void Convert_UnfilledColor_KeepsValidHexOtherwiseNull(string unfilledColor, string? expected) {
        var legacy = Base() with { UnfilledColor = unfilledColor };
        var (definition, _) = LegacyReportUpgrade.Convert(legacy);
        Assert.Equal(expected, definition.View.Unfilled);
    }

    [Fact]
    public void Convert_LabelColors_MapsRarityNamesToIndices() {
        var legacy = Base() with { GroupBy = "rarity", LabelColors = "{\"Common\":\"#111111\",\"bogus\":\"nope\"}" };
        var (definition, _) = LegacyReportUpgrade.Convert(legacy);
        Assert.Equal(1, definition.View.LabelColors.Count);
        Assert.Equal("#111111", definition.View.LabelColors["0"]);
    }

    [Fact]
    public void Convert_Menno_SetsViewOptions() {
        var legacy = Base() with { MennoEnabled = true, MennoCompareMode = "ratio", DisplayMode = "heatmap" };
        var (definition, _) = LegacyReportUpgrade.Convert(legacy);
        Assert.Equal("true", definition.View.Options[LedgerReportKeys.MennoOption]);
        Assert.Equal("ratio", definition.View.Options[LedgerReportKeys.MennoCompareOption]);
        Assert.Equal("heatmap", definition.View.Options[LedgerReportKeys.DisplayModeOption]);
    }

    [Fact]
    public void Convert_Placement_MapsGridAndSortOrderAndGroupId() {
        var legacy = Base() with { Id = "r1", GridX = 1, GridY = 2, GridW = 4, GridH = 3, SortOrder = 7, GroupId = "g1" };
        var (_, placement) = LegacyReportUpgrade.Convert(legacy);
        Assert.Equal("r1", placement.ReportId);
        Assert.Equal(1, placement.X);
        Assert.Equal(2, placement.Y);
        Assert.Equal(4, placement.W);
        Assert.Equal(3, placement.H);
        Assert.Equal(7, placement.Order);
        Assert.Equal("g1", placement.Group);
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(5, 5)]
    public void Convert_Placement_GridWidthDefaultsToTwoWhenZero(int gridW, int expectedW) {
        var legacy = Base() with { GridW = gridW };
        var (_, placement) = LegacyReportUpgrade.Convert(legacy);
        Assert.Equal(expectedW, placement.W);
    }

    [Fact]
    public void Convert_Placement_EmptyGroupIdBecomesNull() {
        var legacy = Base() with { GroupId = "" };
        var (_, placement) = LegacyReportUpgrade.Convert(legacy);
        Assert.Null(placement.Group);
    }

    [Fact]
    public void Convert_RoundTrip_MatchesCodecUpgrade() {
        var legacy = Base() with {
            Id = "r1",
            Name = "Report One",
            Subject = "artifacts",
            Mode = "aggregate",
            GroupBy = "rarity",
            GridX = 1,
            GridY = 2,
            GridW = 3,
            GridH = 4,
            SortOrder = 5,
            GroupId = "g1",
        };
        var json = JsonSerializer.Serialize(legacy);
        var (expectedDefinition, expectedPlacement) = LegacyReportUpgrade.Convert(legacy);
        var (definition, placement) = ReportCodec.Upgrade(json, LegacyReportUpgrade.Upgrades);
        Assert.Equal(expectedDefinition.Title, definition.Title);
        Assert.Equal(expectedDefinition.Query.Source, definition.Query.Source);
        Assert.Equal(expectedDefinition.Query.GroupBy.Count, definition.Query.GroupBy.Count);
        Assert.Equal(expectedPlacement, placement);
    }
}
