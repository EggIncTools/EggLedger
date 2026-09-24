using EggLedger.Domain.Reports.Charts;

namespace EggLedger.Domain.Tests.Reports;


public class ChartGeometryTests {
    [Fact]
    public void Values_SelectsFloatOrIntSeries() {
        Assert.Equal(new[] { 1.0, 2.0 }, ChartGeometry.Values(new long[] { 1, 2 }, new double[] { 9 }, isFloat: false));
        Assert.Equal(new[] { 9.5 }, ChartGeometry.Values(new long[] { 1 }, new double[] { 9.5 }, isFloat: true));
    }

    [Fact]
    public void Points_ScalesAcrossWidth_MatchesVue() {
        var pts = ChartGeometry.Points(new double[] { 0, 5, 10 }, new[] { "a", "b", "c" }, 300, 130);
        Assert.Equal(3, pts.Count);
        Assert.Equal(36, pts[0].X, 5);
        Assert.Equal(70, pts[0].Y, 5);
        Assert.Equal(164, pts[1].X, 5);
        Assert.Equal(40, pts[1].Y, 5);
        Assert.Equal(292, pts[2].X, 5);
        Assert.Equal(10, pts[2].Y, 5);
    }

    [Fact]
    public void Points_EmptyWhenFewerThanTwo() =>
        Assert.Empty(ChartGeometry.Points([5], ["a"], 300, 130));

    [Fact]
    public void AreaPath_ClosesToBaseline() {
        var pts = ChartGeometry.Points(new double[] { 0, 10 }, new[] { "a", "b" }, 300, 130);
        var path = ChartGeometry.AreaPath(pts, 130);
        Assert.StartsWith("M36,70", path);
        Assert.EndsWith("Z", path);

        Assert.Contains(",70", path);
    }

    [Theory]
    [InlineData("2024-03", "Mar '24")]
    [InlineData("2024-15", "W15 '24")]
    [InlineData("2024-03-07", "7 Mar")]
    [InlineData("verylongstringhere", "verylongs...")]
    [InlineData("short", "short")]
    public void FormatXLabel_MatchesVue(string input, string expected) =>
        Assert.Equal(expected, ChartGeometry.FormatXLabel(input));

    [Fact]
    public void YTicks_ThreeTicks_MatchesVue() {
        var ticks = ChartGeometry.YTicks(10, 130, isFloat: false);
        Assert.Equal(3, ticks.Count);
        Assert.Equal(50.2, ticks[0].Y, 4);
        Assert.Equal("3", ticks[0].Label);
        Assert.Equal("7", ticks[1].Label);
        Assert.Equal("10", ticks[2].Label);
    }

    [Fact]
    public void YTicks_EmptyWhenMaxZero() =>
        Assert.Empty(ChartGeometry.YTicks(0, 130, false));

    [Fact]
    public void ThinLabels_KeepsAllUnderLimit() {
        var pts = ChartGeometry.Points(new double[] { 1, 2, 3 }, new[] { "a", "b", "c" }, 300, 130);
        Assert.Equal(3, ChartGeometry.ThinLabels(pts).Count);
    }

    [Fact]
    public void ThinLabels_ThinsAndKeepsLastOverLimit() {
        var vals = Enumerable.Range(0, 25).Select(i => (double)i).ToArray();
        var labels = vals.Select((_, i) => "x" + i).ToArray();
        var pts = ChartGeometry.Points(vals, labels, 300, 130);
        var thinned = ChartGeometry.ThinLabels(pts);
        Assert.True(thinned.Count <= ChartGeometry.MaxLabels);
        Assert.Equal(pts[^1].Label, thinned[^1].Label);
    }

    [Fact]
    public void SeriesValues_ReadsColumnFromRowMajorMatrix() {

        var matrix = new double[] { 1, 2, 3, 4, 5, 6 };
        Assert.Equal(new[] { 2.0, 5.0 }, ChartGeometry.SeriesValues(matrix, 2, 3, 1));
    }

    [Fact]
    public void SeriesColors_SpreadsHueAndHonorsOverrides() {
        var cols = new[] { "A", "B" };
        var colors = ChartGeometry.SeriesColors(cols, "#6366f1", new Dictionary<string, string>());
        Assert.Equal(2, colors.Count);
        Assert.StartsWith("hsl(", colors[0]);

        var withOverride = ChartGeometry.SeriesColors(
            cols, "#6366f1", new Dictionary<string, string> { ["A"] = "#000000" });
        Assert.Equal("#000000", withOverride[0]);
    }

    [Fact]
    public void SeriesColors_GroupedBarOverload_MatchesInlineGeometry() {

        var cols = new[] { "A", "B", "Other" };
        var colors = ChartGeometry.SeriesColors(
            cols,
            "#6366f1",
            new Dictionary<string, string>(),
            hueDenominator: 4,
            otherSentinel: ("Other", "#6b7280"));

        Assert.Equal(3, colors.Count);
        Assert.Equal("hsl(239, 84%, 55%)", colors[0]);
        Assert.Equal("hsl(329, 84%, 55%)", colors[1]);
        Assert.Equal("#6b7280", colors[2]);
    }
}
