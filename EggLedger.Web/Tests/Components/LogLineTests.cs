using EggLedger.Web.Components.Shell;

namespace EggLedger.Web.Tests.Components;

public sealed class LogLineTests {
    [Fact]
    public void PlainText_IsSingleUncoloredRun() {
        var runs = LogLine.Parse("done.");

        Assert.Equal([new LogRun(LogRunKind.Text, "done.")], runs);
    }

    [Fact]
    public void ColorToken_EmitsColoredRunBetweenPlainRuns() {
        var runs = LogLine.Parse("found &148c32<3 completed> missions");

        Assert.Equal([
            new LogRun(LogRunKind.Text, "found "),
            new LogRun(LogRunKind.Text, "3 completed", "#148c32"),
            new LogRun(LogRunKind.Text, " missions")
        ], runs);
    }

    [Fact]
    public void ImageToken_EmitsImageRunWithFileName() {
        var runs = LogLine.Parse("  [img:soul_egg.png] &a855f7<12 SE>");

        Assert.Equal([
            new LogRun(LogRunKind.Text, "  "),
            new LogRun(LogRunKind.Image, "soul_egg.png"),
            new LogRun(LogRunKind.Text, " "),
            new LogRun(LogRunKind.Text, "12 SE", "#a855f7")
        ], runs);
    }

    [Fact]
    public void ImageToken_WithPathSeparator_StaysPlainText() {
        var runs = LogLine.Parse("[img:../x.png]");

        Assert.Equal([new LogRun(LogRunKind.Text, "[img:../x.png]")], runs);
    }

    [Fact]
    public void EidBar_EmitsBarRun() {
        var runs = LogLine.Parse("fetched EI[eid-bar] ok");

        Assert.Equal([
            new LogRun(LogRunKind.Text, "fetched EI"),
            new LogRun(LogRunKind.EidBar, ""),
            new LogRun(LogRunKind.Text, " ok")
        ], runs);
    }

    [Fact]
    public void EidBarInsideColorRun_SplitsColoredText() {
        var runs = LogLine.Parse("&7a7a7a<EI[eid-bar]>");

        Assert.Equal([
            new LogRun(LogRunKind.Text, "EI", "#7a7a7a"),
            new LogRun(LogRunKind.EidBar, "")
        ], runs);
    }

    [Fact]
    public void MalformedColor_RendersAsPlainText() {
        var runs = LogLine.Parse("&zzzzzz<x>");

        Assert.Equal([new LogRun(LogRunKind.Text, "&zzzzzz<x>")], runs);
    }

    [Fact]
    public void UnterminatedColor_RendersAsPlainText() {
        var runs = LogLine.Parse("a &148c32<never closed");

        Assert.Equal([new LogRun(LogRunKind.Text, "a &148c32<never closed")], runs);
    }

    [Fact]
    public void AdjacentTokens_ParseWithoutGaps() {
        var runs = LogLine.Parse("[img:truth_egg.png]&c831ff<1 TE>[eid-bar]&eab308<2 PE>");

        Assert.Equal([
            new LogRun(LogRunKind.Image, "truth_egg.png"),
            new LogRun(LogRunKind.Text, "1 TE", "#c831ff"),
            new LogRun(LogRunKind.EidBar, ""),
            new LogRun(LogRunKind.Text, "2 PE", "#eab308")
        ], runs);
    }

    [Fact]
    public void EmptyString_YieldsNoRuns() {
        Assert.Empty(LogLine.Parse(""));
    }
}
