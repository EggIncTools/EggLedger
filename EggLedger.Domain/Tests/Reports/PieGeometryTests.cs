using EggLedger.Domain.Reports.Charts;

namespace EggLedger.Domain.Tests.Reports;

public class PieGeometryTests {
    [Fact]
    public void SlicePath_HalfCircle_MatchesVue() {

        var path = PieGeometry.SlicePath(100, 100, 50, -Math.PI / 2, Math.PI / 2);
        Assert.Equal("M 100 100 L 100 50 A 50 50 0 0 1 100 150 Z", path);
    }

    [Fact]
    public void SlicePath_SetsLargeArcFlagForSweepOverPi() {
        var path = PieGeometry.SlicePath(0, 0, 10, 0, Math.PI * 1.5);
        Assert.Contains(" 0 1 1 ", path);
    }

    [Fact]
    public void BuildItems_RollsExtraIntoOther() {
        var values = new double[] { 10, 9, 8, 7, 6, 5, 4, 3, 2, 1, 1, 1 };
        var labels = values.Select((_, i) => "L" + i).ToList();
        var items = PieGeometry.BuildItems(labels, values);

        Assert.Equal(10, items.Count);
        Assert.Equal("Other", items[^1].Label);
        Assert.Equal(3, items[^1].Value);
        Assert.Equal("L0", items[0].Label);
    }

    [Fact]
    public void BuildItems_ReturnsEmptyWhenTotalZero() {
        var items = PieGeometry.BuildItems(new[] { "A", "B" }, new double[] { 0, 0 });
        Assert.Empty(items);
    }

    [Fact]
    public void BuildSlices_SweepsProportionalToValueAndColors() {
        var labels = new[] { "A", "B" };
        var values = new double[] { 3, 1 };
        var slices = PieGeometry.BuildSlices(
            labels, values, 100, 100, 50, "#6366f1", new Dictionary<string, string>());

        Assert.Equal(2, slices.Count);
        Assert.Equal(75, slices[0].Pct, 5);
        Assert.Equal(25, slices[1].Pct, 5);
        Assert.Equal("#6366f1", slices[0].Color);
        Assert.Equal(-Math.PI / 2 + 0.75 * Math.PI, slices[0].MidAngle, 9);
        Assert.Equal(Math.PI + 0.25 * Math.PI, slices[1].MidAngle, 9);
    }

    [Fact]
    public void SlicePoint_InsetsAlongMidAngle() {
        var (x, y) = PieGeometry.SlicePoint(100, 100, 50, 0);
        Assert.Equal(130, x, 9);
        Assert.Equal(100, y, 9);
    }

    [Fact]
    public void BuildSlices_HonorsLabelColorOverride() {
        var slices = PieGeometry.BuildSlices(
            new[] { "A", "B" },
            new double[] { 1, 1 },
            100, 100, 50,
            "#6366f1",
            new Dictionary<string, string> { ["A"] = "#abcdef" });
        Assert.Equal("#abcdef", slices[0].Color);
    }
}
