using EggLedger.Domain.Reports;

namespace EggLedger.Domain.Tests.Reports;

public class ValueFilterTests {
    private static ReportResult IntResult(IReadOnlyList<string> labels, IReadOnlyList<long> values) =>
        new() { Labels = [.. labels], Values = [.. values], IsFloat = false };

    private static ReportResult FloatResult(IReadOnlyList<string> labels, IReadOnlyList<double> values) =>
        new() { Labels = [.. labels], Values = [], FloatValues = [.. values], IsFloat = true };

    [Fact]
    public void Apply_NoOp_ReturnsSameResult() {
        var result = IntResult(["a", "b"], [1, 2]);
        var filtered = ValueFilter.Apply(result, "", 0);
        Assert.Same(result, filtered);
    }

    [Fact]
    public void Apply_GreaterThan_KeepsMatchingRows() {
        var result = IntResult(["a", "b", "c"], [1, 5, 10]);
        var filtered = ValueFilter.Apply(result, ">", 4);
        Assert.Equal(["b", "c"], filtered.Labels);
        Assert.Equal([5L, 10L], filtered.Values);
    }

    [Fact]
    public void Apply_LessThanOrEqual_KeepsMatchingRows() {
        var result = IntResult(["a", "b", "c"], [1, 5, 10]);
        var filtered = ValueFilter.Apply(result, "<=", 5);
        Assert.Equal(["a", "b"], filtered.Labels);
    }

    [Fact]
    public void Apply_Equal_KeepsExactMatch() {
        var result = IntResult(["a", "b"], [5, 6]);
        var filtered = ValueFilter.Apply(result, "=", 5);
        Assert.Equal(["a"], filtered.Labels);
    }

    [Fact]
    public void Apply_NotEqual_ExcludesMatch() {
        var result = IntResult(["a", "b"], [5, 6]);
        var filtered = ValueFilter.Apply(result, "!=", 5);
        Assert.Equal(["b"], filtered.Labels);
    }

    [Fact]
    public void Apply_Float_FiltersOnFloatValues() {
        var result = FloatResult(["a", "b"], [1.5, 9.5]);
        var filtered = ValueFilter.Apply(result, ">", 5);
        Assert.Equal(["b"], filtered.Labels);
        Assert.Equal([9.5], filtered.FloatValues);
        Assert.True(filtered.IsFloat);
    }

    [Fact]
    public void Apply_Is2D_ReturnsSameResult() {
        var result = new ReportResult { Is2D = true, Labels = ["a"], Values = [1] };
        var filtered = ValueFilter.Apply(result, ">", 0);
        Assert.Same(result, filtered);
    }
}
