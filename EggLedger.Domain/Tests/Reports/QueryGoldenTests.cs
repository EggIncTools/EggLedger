using EggLedger.Domain.Reports;

namespace EggLedger.Domain.Tests.Reports;

public class QueryGoldenTests {
    private static void AssertQuery(string gotQ, string wantQ, IReadOnlyList<object?> gotArgs, object?[] wantArgs) {
        Assert.Equal(wantQ, gotQ);
        Assert.Equal(wantArgs.Length, gotArgs.Count);
        for (var i = 0; i < gotArgs.Count; i++) {
            Assert.Equal(wantArgs[i], gotArgs[i]);
        }
    }

    [Fact]
    public void AggregateMission() {
        var def = new ReportDefinition { Mode = "aggregate", GroupBy = "ship_type", Subject = "missions" };
        var (q, a) = QueryBuilder.BuildAggregateQuery(def, "m.player_id = ?", new List<object?> { "EI1" });
        const string want = "\n" +
            "            SELECT CAST(m.ship AS TEXT), COUNT(*) AS count\n" +
            "            FROM mission m\n" +
            "            WHERE m.player_id = ?\n" +
            "            GROUP BY m.ship\n" +
            "            ORDER BY count DESC";
        AssertQuery(q, want, a, ["EI1"]);
    }

    [Fact]
    public void AggregateArtifact() {
        var def = new ReportDefinition { Mode = "aggregate", GroupBy = "rarity", Subject = "artifacts" };
        var (q, a) = QueryBuilder.BuildAggregateQuery(def, "m.player_id = ?", new List<object?> { "EI1" });
        const string want = "\n" +
            "            SELECT CAST(d.rarity AS TEXT), COUNT(*) AS count\n" +
            "            FROM artifact_drops d\n" +
            "            JOIN mission m ON d.mission_id = m.mission_id AND d.player_id = m.player_id\n" +
            "            WHERE m.player_id = ? AND d.drop_index >= 0\n" +
            "            GROUP BY d.rarity\n" +
            "            ORDER BY count DESC";
        AssertQuery(q, want, a, ["EI1"]);
    }

    [Fact]
    public void TimeSeriesMission() {
        var def = new ReportDefinition { Mode = "time_series", GroupBy = "time_bucket", TimeBucket = "month", Subject = "missions" };
        var (q, a) = QueryBuilder.BuildTimeSeriesQuery(def, "m.player_id = ?", new List<object?> { "EI1" });
        const string want = "\n" +
            "            SELECT strftime('%Y-%m', datetime(m.start_timestamp, 'unixepoch')) AS bucket, COUNT(*) AS count\n" +
            "            FROM mission m\n" +
            "            WHERE m.player_id = ?\n" +
            "            GROUP BY bucket\n" +
            "            ORDER BY bucket ASC";
        AssertQuery(q, want, a, ["EI1"]);
    }

    [Fact]
    public void TimeSeriesArtifactCustom() {
        var def = new ReportDefinition { Mode = "time_series", GroupBy = "time_bucket", TimeBucket = "custom", CustomBucketN = 3, CustomBucketUnit = "month", Subject = "artifacts" };
        var (q, a) = QueryBuilder.BuildTimeSeriesQuery(def, "m.player_id = ?", new List<object?> { "EI1" });
        const string want = "\n" +
            "            SELECT strftime('%Y-%m', datetime(m.start_timestamp, 'unixepoch')) AS bucket, COUNT(*) AS count\n" +
            "            FROM artifact_drops d\n" +
            "            JOIN mission m ON d.mission_id = m.mission_id AND d.player_id = m.player_id\n" +
            "            WHERE m.player_id = ? AND d.drop_index >= 0 AND m.start_timestamp >= strftime('%s', 'now', ?)\n" +
            "            GROUP BY bucket\n" +
            "            ORDER BY bucket ASC";
        AssertQuery(q, want, a, ["EI1", "-3 months"]);
    }

    [Fact]
    public void PivotMission() {
        var def = new ReportDefinition { Mode = "aggregate", GroupBy = "ship_type", SecondaryGroupBy = "duration_type" };
        var (q, a) = QueryBuilder.BuildPivotQuery(def, "m.player_id = ?", new List<object?> { "EI1" });
        const string want = "\n" +
            "            SELECT CAST(m.ship AS TEXT), CAST(m.duration_type AS TEXT), COUNT(*) AS count\n" +
            "            FROM mission m\n" +
            "            WHERE m.player_id = ?\n" +
            "            GROUP BY m.ship, m.duration_type\n" +
            "            ORDER BY m.ship, m.duration_type";
        AssertQuery(q, want, a, ["EI1"]);
    }

    [Fact]
    public void PivotArtifact() {
        var def = new ReportDefinition { Mode = "aggregate", GroupBy = "rarity", SecondaryGroupBy = "tier" };
        var (q, a) = QueryBuilder.BuildPivotQuery(def, "m.player_id = ?", new List<object?> { "EI1" });
        const string want = "\n" +
            "            SELECT CAST(d.rarity AS TEXT), CAST(d.level AS TEXT), COUNT(*) AS count\n" +
            "            FROM artifact_drops d\n" +
            "            JOIN mission m ON d.mission_id = m.mission_id AND d.player_id = m.player_id\n" +
            "            WHERE m.player_id = ? AND d.drop_index >= 0\n" +
            "            GROUP BY d.rarity, d.level\n" +
            "            ORDER BY d.rarity, d.level";
        AssertQuery(q, want, a, ["EI1"]);
    }

    [Fact]
    public void TimePivotMission() {
        var def = new ReportDefinition { Mode = "time_series", SecondaryGroupBy = "ship_type", TimeBucket = "month" };
        var (q, a) = QueryBuilder.BuildTimePivotQuery(def, "m.player_id = ?", new List<object?> { "EI1" });
        const string want = "\n" +
            "            SELECT strftime('%Y-%m', datetime(m.start_timestamp, 'unixepoch')) AS bucket, CAST(m.ship AS TEXT) AS grp, COUNT(*) AS count\n" +
            "            FROM mission m\n" +
            "            WHERE m.player_id = ?\n" +
            "            GROUP BY bucket, grp\n" +
            "            ORDER BY bucket ASC, grp ASC";
        AssertQuery(q, want, a, ["EI1"]);
    }

    [Fact]
    public void TimePivotArtifactCustom() {
        var def = new ReportDefinition { Mode = "time_series", SecondaryGroupBy = "artifact_name", TimeBucket = "custom", CustomBucketN = 2, CustomBucketUnit = "week" };
        var (q, a) = QueryBuilder.BuildTimePivotQuery(def, "m.player_id = ?", new List<object?> { "EI1" });
        const string want = "\n" +
            "            SELECT strftime('%Y-%W', datetime(m.start_timestamp, 'unixepoch')) AS bucket, CAST(d.artifact_id AS TEXT) AS grp, COUNT(*) AS count\n" +
            "            FROM artifact_drops d\n" +
            "            JOIN mission m ON d.mission_id = m.mission_id AND d.player_id = m.player_id\n" +
            "            WHERE m.player_id = ? AND d.drop_index >= 0 AND m.start_timestamp >= strftime('%s', 'now', ?)\n" +
            "            GROUP BY bucket, grp\n" +
            "            ORDER BY bucket ASC, grp ASC";
        AssertQuery(q, want, a, ["EI1", "-14 days"]);
    }

    [Fact]
    public void WeightedAggregate() {
        var def = new ReportDefinition { Subject = "artifacts", Mode = "aggregate", GroupBy = "ship_type" };
        var (q, a) = QueryBuilder.BuildWeightedAggregateQuery(def, "m.player_id = ?", new List<object?> { "EI1" }, "d.artifact_id IN (?, ?)", new List<object?> { 1, 2 });
        const string want = "\n" +
            "        SELECT CAST(m.ship AS TEXT), CAST(d.artifact_id AS INTEGER), CAST(d.level AS INTEGER),\n" +
            "               SUM(CASE WHEN m.nominal_capacity > 0 AND m.capacity > 0\n" +
            "                        THEN CAST(m.nominal_capacity AS REAL) / CAST(m.capacity AS REAL)\n" +
            "                        ELSE 1.0 END) AS cap_weight\n" +
            "        FROM artifact_drops d\n" +
            "        JOIN mission m ON d.mission_id = m.mission_id AND d.player_id = m.player_id\n" +
            "        WHERE m.player_id = ? AND d.artifact_id IN (?, ?) AND d.drop_index >= 0\n" +
            "        GROUP BY m.ship, d.artifact_id, d.level\n" +
            "        ORDER BY cap_weight DESC";
        AssertQuery(q, want, a, ["EI1", 1, 2]);
    }

    [Fact]
    public void WeightedPivot() {
        var def = new ReportDefinition { Subject = "artifacts", Mode = "aggregate", GroupBy = "ship_type", SecondaryGroupBy = "duration_type" };
        var (q, a) = QueryBuilder.BuildWeightedPivotQuery(def, "m.player_id = ?", new List<object?> { "EI1" }, "d.artifact_id IN (?)", new List<object?> { 1 });
        const string want = "\n" +
            "        SELECT CAST(m.ship AS TEXT), CAST(m.duration_type AS TEXT), CAST(d.artifact_id AS INTEGER), CAST(d.level AS INTEGER),\n" +
            "               SUM(CASE WHEN m.nominal_capacity > 0 AND m.capacity > 0\n" +
            "                        THEN CAST(m.nominal_capacity AS REAL) / CAST(m.capacity AS REAL)\n" +
            "                        ELSE 1.0 END) AS cap_weight\n" +
            "        FROM artifact_drops d\n" +
            "        JOIN mission m ON d.mission_id = m.mission_id AND d.player_id = m.player_id\n" +
            "        WHERE m.player_id = ? AND d.artifact_id IN (?) AND d.drop_index >= 0\n" +
            "        GROUP BY m.ship, m.duration_type, d.artifact_id, d.level\n" +
            "        ORDER BY m.ship, m.duration_type";
        AssertQuery(q, want, a, ["EI1", 1]);
    }

    [Fact]
    public void WeightedTimeSeriesCustom() {
        var def = new ReportDefinition { Subject = "artifacts", Mode = "time_series", TimeBucket = "custom", CustomBucketN = 4, CustomBucketUnit = "day" };
        var (q, a) = QueryBuilder.BuildWeightedTimeSeriesQuery(def, "m.player_id = ?", new List<object?> { "EI1" }, "d.artifact_id IN (?)", new List<object?> { 1 });
        const string want = "\n" +
            "        SELECT strftime('%Y-%m-%d', datetime(m.start_timestamp, 'unixepoch')) AS bucket, CAST(d.artifact_id AS INTEGER), CAST(d.level AS INTEGER),\n" +
            "               SUM(CASE WHEN m.nominal_capacity > 0 AND m.capacity > 0\n" +
            "                        THEN CAST(m.nominal_capacity AS REAL) / CAST(m.capacity AS REAL)\n" +
            "                        ELSE 1.0 END) AS cap_weight\n" +
            "        FROM artifact_drops d\n" +
            "        JOIN mission m ON d.mission_id = m.mission_id AND d.player_id = m.player_id\n" +
            "        WHERE m.player_id = ? AND d.artifact_id IN (?) AND d.drop_index >= 0 AND m.start_timestamp >= strftime('%s', 'now', ?)\n" +
            "        GROUP BY bucket, d.artifact_id, d.level\n" +
            "        ORDER BY bucket ASC";
        AssertQuery(q, want, a, ["EI1", 1, "-4 days"]);
    }

    [Fact]
    public void WeightedTimeSeries() {
        var def = new ReportDefinition { Subject = "artifacts", Mode = "time_series", TimeBucket = "month" };
        var (q, a) = QueryBuilder.BuildWeightedTimeSeriesQuery(def, "m.player_id = ?", new List<object?> { "EI1" }, "d.artifact_id IN (?)", new List<object?> { 1 });
        const string want = "\n" +
            "        SELECT strftime('%Y-%m', datetime(m.start_timestamp, 'unixepoch')) AS bucket, CAST(d.artifact_id AS INTEGER), CAST(d.level AS INTEGER),\n" +
            "               SUM(CASE WHEN m.nominal_capacity > 0 AND m.capacity > 0\n" +
            "                        THEN CAST(m.nominal_capacity AS REAL) / CAST(m.capacity AS REAL)\n" +
            "                        ELSE 1.0 END) AS cap_weight\n" +
            "        FROM artifact_drops d\n" +
            "        JOIN mission m ON d.mission_id = m.mission_id AND d.player_id = m.player_id\n" +
            "        WHERE m.player_id = ? AND d.artifact_id IN (?) AND d.drop_index >= 0\n" +
            "        GROUP BY bucket, d.artifact_id, d.level\n" +
            "        ORDER BY bucket ASC";
        AssertQuery(q, want, a, ["EI1", 1]);
    }

    [Fact]
    public void WeightedTimePivot() {
        var def = new ReportDefinition { Subject = "artifacts", Mode = "time_series", TimeBucket = "month", SecondaryGroupBy = "ship_type" };
        var (q, a) = QueryBuilder.BuildWeightedTimePivotQuery(def, "m.player_id = ?", new List<object?> { "EI1" }, "d.artifact_id IN (?)", new List<object?> { 1 });
        const string want = "\n" +
            "        SELECT strftime('%Y-%m', datetime(m.start_timestamp, 'unixepoch')) AS bucket, CAST(m.ship AS TEXT) AS grp, CAST(d.artifact_id AS INTEGER), CAST(d.level AS INTEGER),\n" +
            "               SUM(CASE WHEN m.nominal_capacity > 0 AND m.capacity > 0\n" +
            "                        THEN CAST(m.nominal_capacity AS REAL) / CAST(m.capacity AS REAL)\n" +
            "                        ELSE 1.0 END) AS cap_weight\n" +
            "        FROM artifact_drops d\n" +
            "        JOIN mission m ON d.mission_id = m.mission_id AND d.player_id = m.player_id\n" +
            "        WHERE m.player_id = ? AND d.artifact_id IN (?) AND d.drop_index >= 0\n" +
            "        GROUP BY bucket, grp, d.artifact_id, d.level\n" +
            "        ORDER BY bucket ASC, grp ASC";
        AssertQuery(q, want, a, ["EI1", 1]);
    }
}
