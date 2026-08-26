using EggLedger.Domain.Reports;

namespace EggLedger.Domain.Tests.Reports;



public class ExecuteTests {

    private sealed class FakeDb : IMissionDb {
        private readonly Dictionary<string, IReadOnlyList<object?[]>> _byPrefix = new(StringComparer.Ordinal);
        public List<(string sql, IReadOnlyList<object?> args)> Calls { get; } = [];


        public FakeDb On(string contains, IReadOnlyList<object?[]> rows) {
            _byPrefix[contains] = rows;
            return this;
        }

        public IReadOnlyList<object?[]> Query(string sql, IReadOnlyList<object?> args) {
            Calls.Add((sql, args));
            foreach (var (key, rows) in _byPrefix) {
                if (sql.Contains(key, StringComparison.Ordinal)) {
                    return rows;
                }
            }
            return Array.Empty<object?[]>();
        }
    }

    private sealed class NoWeights : IWeightData {
        public double CraftingWeight(long artifactId, long level) => 1;
        public IReadOnlyList<int> FamilyAfxIds(string familyId) => Array.Empty<int>();
    }

    private sealed class FixedWeights : IWeightData {
        public double CraftingWeight(long artifactId, long level) => 1;
        public IReadOnlyList<int> FamilyAfxIds(string familyId) => new[] { 1, 2 };
    }

    [Fact]
    public void ExecuteReport_Aggregate_FormatsShipLabels() {

        var db = new FakeDb().On("GROUP BY m.ship", new object?[][]
        {
            ["9", 10L],
            ["3", 4L],
        });
        var ex = new ReportExecutor(db, new NoWeights());
        var def = new ReportDefinition { Mode = "aggregate", GroupBy = "ship_type", Subject = "missions", AccountId = "EI1" };

        var result = ex.ExecuteReport(def);

        Assert.False(result.Is2D);
        Assert.False(result.IsFloat);
        Assert.Equal([10, 4], result.Values);

        Assert.Equal("Henerprise", result.Labels[0]);
        Assert.Equal("BCR", result.Labels[1]);
        Assert.Equal("EI1", db.Calls[0].args[0]);
    }

    [Fact]
    public void ExecuteReport_Pivot_ProducesSortedMatrix() {
        var db = new FakeDb().On("GROUP BY m.ship, m.duration_type", new object?[][]
        {
            ["3", "1", 2L],
            ["9", "0", 5L],
            ["9", "1", 7L],
        });
        var ex = new ReportExecutor(db, new NoWeights());
        var def = new ReportDefinition {
            Mode = "aggregate",
            GroupBy = "ship_type",
            SecondaryGroupBy = "duration_type",
            AccountId = "EI1",
        };

        var result = ex.ExecuteReport(def);

        Assert.True(result.Is2D);

        Assert.Equal(["BCR", "Henerprise"], result.RowLabels);

        Assert.Equal(["Short", "Standard"], result.ColLabels);

        Assert.Equal([0, 2, 5, 7], result.MatrixValues);
    }

    [Fact]
    public void ExecuteReport_FamilyWeighted_UsesWeightedPath() {


        var db = new FakeDb().On("cap_weight", new object?[][]
        {
            ["9", 1L, 0L, 2.0],
            ["3", 2L, 0L, 1.0],
        });
        var ex = new ReportExecutor(db, new FixedWeights());
        var def = new ReportDefinition {
            Subject = "artifacts",
            Mode = "aggregate",
            GroupBy = "ship_type",
            FamilyWeight = "tachyon-stone",
            AccountId = "EI1",
        };

        var result = ex.ExecuteReport(def);

        Assert.True(result.IsFloat);

        Assert.Equal([2.0, 1.0], result.FloatValues);
    }

    [Fact]
    public void ExecuteReport_UnknownMode_Throws() {
        var ex = new ReportExecutor(new FakeDb(), new NoWeights());
        var def = new ReportDefinition { Mode = "bogus", AccountId = "EI1" };
        Assert.Throws<InvalidOperationException>(() => ex.ExecuteReport(def));
    }
}
