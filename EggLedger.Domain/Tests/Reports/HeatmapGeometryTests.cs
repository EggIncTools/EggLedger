using EggLedger.Domain.Reports.Charts;

namespace EggLedger.Domain.Tests.Reports;


public class HeatmapGeometryTests {
    [Fact]
    public void CellIntensity_DefaultIsValueOverMax() =>
        Assert.Equal(0.5, HeatmapGeometry.CellIntensity(5, 10, "none", null), 5);

    [Fact]
    public void CellIntensity_FloorsAtTwelvePercent() =>
        Assert.Equal(0.12, HeatmapGeometry.CellIntensity(0.5, 100, "none", null), 5);

    [Fact]
    public void CellIntensity_RatioAndPctModes() {
        Assert.Equal(0.5, HeatmapGeometry.CellIntensity(1, 0, "ratio", null), 5);
        Assert.Equal(0.5, HeatmapGeometry.CellIntensity(50, 0, "row_pct", null), 5);
    }

    [Fact]
    public void CellStyle_BlendsBaseOverUnfilled_MatchesVue() {
        var style = HeatmapGeometry.CellStyle(5, 10, "#6366f1", "#1f2937", "none", null, belowThreshold: false);
        Assert.Equal("rgb(65, 72, 148)", style.BackgroundColor);
        Assert.Equal("#f3f4f6", style.Color);
    }

    [Fact]
    public void CellStyle_ZeroUsesUnfilled_MatchesVue() {
        var style = HeatmapGeometry.CellStyle(0, 10, "#6366f1", "#1f2937", "none", null, belowThreshold: false);
        Assert.Equal("#1f2937", style.BackgroundColor);
        Assert.Equal("#6b7280", style.Color);
    }

    [Fact]
    public void CellStyle_BelowThresholdUsesUnfilled() {
        var style = HeatmapGeometry.CellStyle(9, 10, "#6366f1", "#1f2937", "none", null, belowThreshold: true);
        Assert.Equal("#1f2937", style.BackgroundColor);
    }

    [Fact]
    public void RelativeLuminance_WhiteIsOne() =>
        Assert.Equal(1.0, HeatmapGeometry.RelativeLuminance(255, 255, 255), 5);

    [Theory]
    [InlineData(5, "none", false, "5")]
    [InlineData(0, "none", false, "0")]
    [InlineData(3.5, "row_pct", false, "3.5%")]
    [InlineData(2, "ratio", false, "x2.00")]
    [InlineData(7.25, "none", false, "7.25")]
    [InlineData(9, "none", true, "-")]
    public void DisplayValue_MatchesVue(double v, string normalizeBy, bool below, string expected) =>
        Assert.Equal(expected, HeatmapGeometry.DisplayValue(v, normalizeBy, below));

    [Fact]
    public void IsBelowThreshold_RespectsDisabledAndNullCount() {
        Assert.False(HeatmapGeometry.IsBelowThreshold(1, 0));
        Assert.False(HeatmapGeometry.IsBelowThreshold(null, 5));
        Assert.True(HeatmapGeometry.IsBelowThreshold(2, 5));
        Assert.False(HeatmapGeometry.IsBelowThreshold(5, 5));
    }
}
