using EggLedger.Domain.Reports;

namespace EggLedger.Domain.Tests.Reports;


public class ReportGridLayoutTests {
    private static ReportDefinition Def(int w, int h) => new() { GridW = w, GridH = h };

    [Theory]
    [InlineData(0, 0, 1, 1)]
    [InlineData(3, 4, 3, 4)]
    [InlineData(99, 99, 8, 8)]
    [InlineData(-2, -5, 1, 1)]
    public void ClampDims_ClampsToRange(int w, int h, int ew, int eh) {
        var (cw, ch) = ReportGridLayout.ClampDims(w, h);
        Assert.Equal(ew, cw);
        Assert.Equal(eh, ch);
    }

    [Fact]
    public void BuildOccupancy_PacksRowMajorWrappingAtEightCols() {

        var defs = new[] { Def(4, 2), Def(4, 2), Def(4, 2) };
        var (positions, _) = ReportGridLayout.BuildOccupancyFromLayout(defs);

        Assert.Equal(new GridCardPos(1, 1, 4, 2, 0), positions[0]);
        Assert.Equal(new GridCardPos(5, 1, 4, 2, 1), positions[1]);
        Assert.Equal(new GridCardPos(1, 3, 4, 2, 2), positions[2]);
    }

    [Fact]
    public void BuildOccupancy_FillsGapLeftByTallNeighbor() {

        var defs = new[] { Def(2, 2), Def(2, 1) };
        var (positions, occupied) = ReportGridLayout.BuildOccupancyFromLayout(defs);

        Assert.Equal(new GridCardPos(1, 1, 2, 2, 0), positions[0]);
        Assert.Equal(new GridCardPos(3, 1, 2, 1, 1), positions[1]);
        Assert.Contains((1, 1), occupied);
        Assert.Contains((2, 2), occupied);
        Assert.Contains((3, 1), occupied);
        Assert.DoesNotContain((1, 3), occupied);
    }

    [Fact]
    public void CellsFit_DetectsOverlap() {
        var occupied = new HashSet<(int, int)>();
        ReportGridLayout.MarkOccupied(occupied, 1, 1, 2, 2);
        Assert.False(ReportGridLayout.CellsFit(occupied, 2, 2, 2, 2));
        Assert.True(ReportGridLayout.CellsFit(occupied, 3, 1, 2, 2));
    }

    [Fact]
    public void FindPlacement_WrapsToNextRowWhenOverflowing() {
        var occupied = new HashSet<(int, int)>();

        var pos = ReportGridLayout.FindPlacement(occupied, 6, 1, 5, 1);
        Assert.Equal((1, 2), pos);
    }

    [Fact]
    public void ComputeEmptyZones_FindsTrailingGapOnLastRow() {

        var defs = new[] { Def(4, 1) };
        var zones = ReportGridLayout.ComputeEmptyZones(defs);

        Assert.Single(zones);
        Assert.Equal(5, zones[0].ColStart);
        Assert.Equal(9, zones[0].ColEnd);
        Assert.Equal(1, zones[0].RowStart);
        Assert.Equal(0, zones[0].InsertAfter);
    }

    [Fact]
    public void ComputeEmptyZones_EmptyForNoDefs() {
        Assert.Empty(ReportGridLayout.ComputeEmptyZones(Array.Empty<ReportDefinition>()));
    }

    [Theory]
    [InlineData(800, 89)]
    [InlineData(1600, 189)]
    [InlineData(100, 80)]
    [InlineData(0, 80)]
    public void ComputeRowHeightPx_MatchesGoFormula(double containerWidthPx, int expected) {
        Assert.Equal(expected, ReportGridLayout.ComputeRowHeightPx(containerWidthPx));
    }
}
