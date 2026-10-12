using EggIdentity.Filters;
using EggLedger.Domain.Reports;

namespace EggLedger.Domain.Tests.Reports;

public class QueryTests {
    private static FilterSet And(params FilterCondition[] conditions) => new(conditions, []);

    [Fact]
    public void BuildWhereClause_MissionScope() {
        var filters = And(new FilterCondition("ship", "=", "3"), new FilterCondition("duration", "!=", "0"));
        var (clause, args) = QueryBuilder.BuildWhereClause(filters);
        Assert.Contains("m.ship = ?", clause);
        Assert.Contains("m.duration_type != ?", clause);
        Assert.Equal(2, args.Count);
    }

    [Fact]
    public void BuildWhereClause_ArtifactScope() {
        var filters = And(new FilterCondition("artifact_rarity", ">=", "2"), new FilterCondition("artifact_spec_type", "=", "Artifact"));
        var (clause, args) = QueryBuilder.BuildWhereClause(filters);
        Assert.Contains("d.rarity >= ?", clause);
        Assert.Contains("d.spec_type = ?", clause);
        Assert.Equal(2, args.Count);
    }

    [Fact]
    public void BuildWhereClause_BooleanOps() {
        var filters = And(new FilterCondition("dubcap", "true", ""), new FilterCondition("buggedcap", "false", ""));
        var (clause, args) = QueryBuilder.BuildWhereClause(filters);
        Assert.Contains("m.is_dub_cap = 1", clause);
        Assert.Contains("m.is_bugged_cap = 0", clause);
        Assert.Empty(args);
    }

    [Fact]
    public void BuildWhereClause_InlineBoolForm() {
        var (clause, _) = QueryBuilder.BuildWhereClause(And(new FilterCondition("dubcap", "=", "true")));
        Assert.Contains("m.is_dub_cap = 1", clause);
    }

    [Fact]
    public void BuildWhereClause_OrSiblingsRescueTheirRow() {
        var filters = new FilterSet(
            [new FilterCondition("ship", "=", "3"), new FilterCondition("level", "=", "5")],
            [[new FilterCondition("ship", "=", "4")], []]);
        var (clause, args) = QueryBuilder.BuildWhereClause(filters);
        Assert.Equal("(m.ship = ? OR m.ship = ?) AND m.level = ?", clause);
        Assert.Equal(["3", "4", "5"], args);
    }

    [Fact]
    public void BuildWhereClause_AliasOpsAndFarmKeyApply() {
        var (clause, args) = QueryBuilder.BuildWhereClause(And(new FilterCondition("farm", "eq", "1")));
        Assert.Equal("m.mission_type = ?", clause);
        Assert.Equal(["1"], args);
    }

    [Fact]
    public void GroupByColumn_Mappings() {
        var cases = new Dictionary<string, string> {
            ["ship_type"] = "m.ship",
            ["duration_type"] = "m.duration_type",
            ["rarity"] = "d.rarity",
            ["tier"] = "d.level",
            ["spec_type"] = "d.spec_type",
        };
        foreach (var (groupBy, want) in cases) {
            Assert.Equal(want, QueryBuilder.GroupByColumn(groupBy));
        }
    }

    public static IEnumerable<object?[]> NumericValidationCases() => new[] {
        new object?[] { new FilterCondition("artifact_tier", "=", "3"), "d.level = ?", "3", false },
        [new FilterCondition("artifact_rarity", ">=", "2"), "d.rarity >= ?", "2", false],
        [new FilterCondition("level", "=", "5"), "m.level = ?", "5", false],
        [new FilterCondition("ship", "=", "3"), "m.ship = ?", "3", false],
        [new FilterCondition("artifact_name", "=", "12"), "d.artifact_id = ?", "12", false],
        [new FilterCondition("artifact_quality", ">", "3.5"), "d.quality > ?", "3.5", false],
        [new FilterCondition("artifact_spec_type", "=", "Artifact"), "d.spec_type = ?", "Artifact", false],
        [new FilterCondition("artifact_tier", "=", "gusset"), null, null, true],
        [new FilterCondition("artifact_rarity", "=", "rare"), null, null, true],
        [new FilterCondition("ship", "=", "henliner"), null, null, true],
        [new FilterCondition("artifact_quality", "=", "abc"), null, null, true],
    };

    [Theory]
    [MemberData(nameof(NumericValidationCases))]
    public void BuildWhereClause_NumericValidation(FilterCondition cond, string? wantSub, string? wantArg, bool wantEmpty) {
        var filters = And(cond);
        var (clause, args) = QueryBuilder.BuildWhereClause(filters);
        if (wantEmpty) {
            Assert.Equal("", clause);
            Assert.Empty(args);
            return;
        }
        Assert.Contains(wantSub!, clause);
        Assert.Single(args);
        Assert.Equal(wantArg, args[0]);
    }

    [Fact]
    public void ConditionToSql_LaunchDTUsesStrftime() {
        var filters = And(new FilterCondition("launchDT", ">=", "2025-01-01"));
        var (clause, args) = QueryBuilder.BuildWhereClause(filters);
        Assert.Contains("m.start_timestamp >= strftime('%s', ?)", clause);
        Assert.Single(args);
        Assert.Equal("2025-01-01", args[0]);
    }

    [Fact]
    public void ConditionToSql_ReturnDTLessThan() {
        var filters = And(new FilterCondition("returnDT", "<", "2025-06-01"));
        var (clause, args) = QueryBuilder.BuildWhereClause(filters);
        Assert.Contains("m.return_timestamp < strftime('%s', ?)", clause);
        Assert.Single(args);
    }

    [Fact]
    public void ConditionToSql_LaunchDTEquals() {
        var filters = And(new FilterCondition("launchDT", "=", "2025-05-15"));
        var (clause, args) = QueryBuilder.BuildWhereClause(filters);
        Assert.Contains("m.start_timestamp = strftime('%s', ?)", clause);
        Assert.Single(args);
        Assert.Equal("2025-05-15", args[0]);
    }

    [Fact]
    public void BuildTimePivotQuery_MissionDimensions() {
        var def = new ReportDefinition {
            Mode = "time_series",
            GroupBy = "time_bucket",
            SecondaryGroupBy = "ship_type",
            TimeBucket = "month",
            AccountId = "EI1234",
        };
        var (query, outArgs) = QueryBuilder.BuildTimePivotQuery(def, "m.player_id = ?", new List<object?> { "EI1234" });
        Assert.Contains("strftime('%Y-%m'", query);
        Assert.Contains("m.ship", query);
        Assert.DoesNotContain("artifact_drops", query);
        Assert.Contains("GROUP BY bucket, grp", query);
        Assert.True(outArgs.Count >= 1);
    }

    [Fact]
    public void BuildTimePivotQuery_ArtifactSecondary_JoinsDrops() {
        var def = new ReportDefinition {
            Mode = "time_series",
            GroupBy = "time_bucket",
            SecondaryGroupBy = "artifact_name",
            TimeBucket = "month",
            AccountId = "EI1234",
        };
        var (query, _) = QueryBuilder.BuildTimePivotQuery(def, "m.player_id = ?", new List<object?> { "EI1234" });
        Assert.Contains("artifact_drops", query);
        Assert.Contains("d.artifact_id", query);
    }

    [Fact]
    public void BuildTimePivotQuery_CustomBucket_AddsWindowCondition() {
        var def = new ReportDefinition {
            Mode = "time_series",
            GroupBy = "time_bucket",
            SecondaryGroupBy = "duration_type",
            TimeBucket = "custom",
            CustomBucketN = 3,
            CustomBucketUnit = "month",
            AccountId = "EI1234",
        };
        var (query, outArgs) = QueryBuilder.BuildTimePivotQuery(def, "m.player_id = ?", new List<object?> { "EI1234" });
        Assert.Contains("strftime('%s'", query);
        Assert.True(outArgs.Count >= 2);
    }

    [Fact]
    public void BuildTimePivotQuery_InvalidSecondary_Throws() {
        var def = new ReportDefinition {
            Mode = "time_series",
            SecondaryGroupBy = "nonexistent_dimension",
            TimeBucket = "month",
        };
        Assert.Throws<InvalidOperationException>(() =>
            QueryBuilder.BuildTimePivotQuery(def, "1=1", []));
    }

    [Fact]
    public void FamilyWeightClause_Empty() {
        var (clause, args) = QueryBuilder.FamilyWeightClause([]);
        Assert.Equal("", clause);
        Assert.Empty(args);
    }

    [Fact]
    public void FamilyWeightClause_SingleId() {
        var (clause, args) = QueryBuilder.FamilyWeightClause(new[] { 1 });
        Assert.Contains("d.artifact_id IN", clause);
        Assert.Single(args);
        Assert.Equal(1, args[0]);
    }

    [Fact]
    public void FamilyWeightClause_MultipleIds() {
        var (clause, args) = QueryBuilder.FamilyWeightClause(new[] { 1, 2, 23 });
        Assert.Contains("?, ?, ?", clause);
        Assert.Equal(3, args.Count);
    }

    [Fact]
    public void BuildWeightedAggregateQuery_IncludesHiddenColumns() {
        var def = new ReportDefinition { Subject = "artifacts", Mode = "aggregate", GroupBy = "ship_type", FamilyWeight = "tachyon-stone" };
        var (query, args) = QueryBuilder.BuildWeightedAggregateQuery(
            def, "m.player_id = ?", new List<object?> { "EI1234" }, "d.artifact_id IN (?, ?)", new List<object?> { 1, 2 });
        Assert.Contains("d.artifact_id", query);
        Assert.Contains("d.level", query);
        Assert.Contains("artifact_drops", query);
        Assert.Equal(3, args.Count);
    }

    [Fact]
    public void BuildWeightedPivotQuery_IncludesHiddenColumns() {
        var def = new ReportDefinition { Subject = "artifacts", Mode = "aggregate", GroupBy = "ship_type", SecondaryGroupBy = "duration_type", FamilyWeight = "tachyon-stone" };
        var (query, args) = QueryBuilder.BuildWeightedPivotQuery(
            def, "m.player_id = ?", new List<object?> { "EI1234" }, "d.artifact_id IN (?)", new List<object?> { 1 });
        Assert.Contains("d.artifact_id", query);
        Assert.Contains("d.level", query);
        Assert.Equal(2, args.Count);
    }

    [Fact]
    public void BuildWeightedTimeSeriesQuery_IncludesHiddenColumns() {
        var def = new ReportDefinition { Subject = "artifacts", Mode = "time_series", TimeBucket = "month", FamilyWeight = "tachyon-stone" };
        var (query, args) = QueryBuilder.BuildWeightedTimeSeriesQuery(
            def, "m.player_id = ?", new List<object?> { "EI1234" }, "d.artifact_id IN (?)", new List<object?> { 1 });
        Assert.Contains("d.artifact_id", query);
        Assert.Contains("d.level", query);
        Assert.Contains("bucket", query);
        Assert.Equal(2, args.Count);
    }

    [Fact]
    public void BuildWeightedTimePivotQuery_IncludesHiddenColumns() {
        var def = new ReportDefinition { Subject = "artifacts", Mode = "time_series", TimeBucket = "month", SecondaryGroupBy = "ship_type", FamilyWeight = "tachyon-stone" };
        var (query, args) = QueryBuilder.BuildWeightedTimePivotQuery(
            def, "m.player_id = ?", new List<object?> { "EI1234" }, "d.artifact_id IN (?)", new List<object?> { 1 });
        Assert.Contains("d.artifact_id", query);
        Assert.Contains("d.level", query);
        Assert.Contains("grp", query);
        Assert.Equal(2, args.Count);
    }
}
