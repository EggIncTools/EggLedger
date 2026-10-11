using EggLedger.Domain.Reports;

namespace EggLedger.Domain.Tests.Reports;

public class MennoComparisonTests {
    private static ReportResult Heatmap2D(
        List<double> matrix,
        List<double>? perMission = null,
        List<double>? airtime = null) => new() {
            Is2D = true,
            IsFloat = true,
            RowLabels = ["a", "b"],
            ColLabels = ["x", "y"],
            RawRowLabels = ["0", "1"],
            RawColLabels = ["0", "1"],
            MatrixValues = matrix,
            RawPerMissionValues = perMission,
            AirtimeMatrixValues = airtime,
        };

    private static ReportDefinition Def(
        bool menno = true,
        string display = "heatmap",
        string mode = "side_by_side",
        string normalize = "none") => new() {
            MennoEnabled = menno,
            DisplayMode = display,
            MennoCompareMode = mode,
            NormalizeBy = normalize,
        };

    [Fact]
    public void ShouldRender_TrueForEnabled2DHeatmapWithMennoResult() {
        var user = Heatmap2D([1, 2, 3, 4]);
        var menno = Heatmap2D([1, 1, 1, 1]);
        Assert.True(MennoComparison.ShouldRender(Def(), user, menno));
    }

    [Fact]
    public void ShouldRender_FalseWhenMennoDisabled() {
        var user = Heatmap2D([1, 2, 3, 4]);
        var menno = Heatmap2D([1, 1, 1, 1]);
        Assert.False(MennoComparison.ShouldRender(Def(menno: false), user, menno));
    }

    [Fact]
    public void ShouldRender_FalseForNonHeatmapDisplay() {
        var user = Heatmap2D([1, 2, 3, 4]);
        var menno = Heatmap2D([1, 1, 1, 1]);
        Assert.False(MennoComparison.ShouldRender(Def(display: "grouped_bar"), user, menno));
    }

    [Fact]
    public void ShouldRender_FalseWhenComparisonMissing() {
        var user = Heatmap2D([1, 2, 3, 4]);
        Assert.False(MennoComparison.ShouldRender(Def(), user, null));
    }

    [Fact]
    public void ShouldRender_FalseWhenUserResultNot2D() {
        var user = new ReportResult { Is2D = false };
        var menno = Heatmap2D([1, 1, 1, 1]);
        Assert.False(MennoComparison.ShouldRender(Def(), user, menno));
    }

    [Fact]
    public void MennoAirtimeResult_SubstitutesAirtimeWhenNormalizeAirtime() {
        var menno = Heatmap2D(
            [10, 20, 30, 40],
            airtime: [1, 2, 3, 4]);
        var res = MennoComparison.MennoAirtimeResult(Def(normalize: "airtime"), menno);
        Assert.Equal([1, 2, 3, 4], res.MatrixValues);
    }

    [Fact]
    public void MennoAirtimeResult_UnchangedWhenNotAirtime() {
        var menno = Heatmap2D(
            [10, 20, 30, 40],
            airtime: [1, 2, 3, 4]);
        var res = MennoComparison.MennoAirtimeResult(Def(normalize: "none"), menno);
        Assert.Same(menno, res);
    }

    [Fact]
    public void MennoAirtimeResult_UnchangedWhenNoAirtimeValues() {
        var menno = Heatmap2D([10, 20, 30, 40]);
        var res = MennoComparison.MennoAirtimeResult(Def(normalize: "airtime"), menno);
        Assert.Same(menno, res);
    }

    [Fact]
    public void UserPerMissionResult_SubstitutesPerMissionWhenPresent() {
        var user = Heatmap2D(
            [100, 200, 300, 400],
            perMission: [1, 2, 3, 4]);
        var res = MennoComparison.UserPerMissionResult(user);
        Assert.Equal([1, 2, 3, 4], res.MatrixValues);
    }

    [Fact]
    public void UserPerMissionResult_UnchangedWhenNoPerMission() {
        var user = Heatmap2D([100, 200, 300, 400]);
        var res = MennoComparison.UserPerMissionResult(user);
        Assert.Same(user, res);
    }

    [Fact]
    public void RatioMatrix_DividesUserByMenno() {
        var user = Heatmap2D([10, 20, 30, 40]);
        var menno = Heatmap2D([5, 10, 10, 8]);
        var ratio = MennoComparison.RatioMatrix(user, menno);
        Assert.Equal(new double?[] { 2, 2, 3, 5 }, ratio);
    }

    [Fact]
    public void RatioMatrix_NullWhenMennoZero() {
        var user = Heatmap2D([10, 20]);
        var menno = Heatmap2D([0, 4]);
        var ratio = MennoComparison.RatioMatrix(user, menno);
        Assert.Null(ratio[0]);
        Assert.Equal(5, ratio[1]);
    }

    [Fact]
    public void RatioMatrix_PrefersPerMissionNumerator() {
        var user = Heatmap2D(
            [100, 200],
            perMission: [10, 20]);
        var menno = Heatmap2D([5, 10]);
        var ratio = MennoComparison.RatioMatrix(user, menno);
        Assert.Equal(new double?[] { 2, 2 }, ratio);
    }

    [Fact]
    public void RatioResult_FlattensNullsToZeroAndSetsFloat() {
        var user = Heatmap2D([10, 20]);
        var menno = Heatmap2D([0, 4]);
        var res = MennoComparison.RatioResult(user, menno);
        Assert.True(res.IsFloat);
        Assert.Equal([0, 5], res.MatrixValues);
        Assert.Equal(user.RowLabels, res.RowLabels);
    }

    [Fact]
    public void RatioColorValues_MatchRatioWithNullsAsZero() {
        var user = Heatmap2D([10, 20]);
        var menno = Heatmap2D([0, 4]);
        var cv = MennoComparison.RatioColorValues(user, menno);
        Assert.Equal(new List<double> { 0, 5 }, cv);
    }
}
