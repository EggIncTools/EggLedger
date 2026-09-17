using EggLedger.Domain.Util;

namespace EggLedger.Desktop.Tests;

public sealed class SemVersionTests {
    [Theory]

    [InlineData("1.0.0", "2.0.0", -1)]
    [InlineData("2.0.0", "1.0.0", 1)]
    [InlineData("1.2.3", "1.2.3", 0)]
    [InlineData("1.2.3", "1.2.4", -1)]
    [InlineData("1.3.0", "1.2.9", 1)]
    [InlineData("2.1.4", "2.1.3", 1)]
    [InlineData("2.1.4", "2.1.5", -1)]

    [InlineData("v1.2.3", "1.2.3", 0)]
    [InlineData("v2.0.0", "v1.0.0", 1)]

    [InlineData("1.2", "1.2.0", 0)]
    [InlineData("1", "1.0.0", 0)]
    [InlineData("1.2", "1.2.1", -1)]

    [InlineData("1.2.3+build1", "1.2.3+build2", 0)]
    [InlineData("1.2.3+meta", "1.2.3", 0)]
    [InlineData("1.2.3+a", "1.2.4", -1)]

    [InlineData("1.0.0-alpha", "1.0.0", -1)]
    [InlineData("1.0.0", "1.0.0-alpha", 1)]
    [InlineData("1.0.0-alpha", "1.0.0-alpha", 0)]

    [InlineData("1.0.0-alpha", "1.0.0-beta", -1)]
    [InlineData("1.0.0-beta", "1.0.0-alpha", 1)]
    [InlineData("1.0.0-alpha", "1.0.0-alpha.1", -1)]
    [InlineData("1.0.0-alpha.1", "1.0.0-alpha.beta", -1)]
    [InlineData("1.0.0-1", "1.0.0-2", -1)]
    [InlineData("1.0.0-2", "1.0.0-10", -1)]

    [InlineData("1.0.0-rc.1+x", "1.0.0-rc.1+y", 0)]
    [InlineData("1.0.0-rc.1", "1.0.0", -1)]
    public void CompareTo_MatchesGoVersionSemantics(string a, string b, int expectedSign) {
        var va = SemVersion.Parse(a);
        var vb = SemVersion.Parse(b);

        Assert.Equal(expectedSign, Math.Sign(va.CompareTo(vb)));

        Assert.Equal(-expectedSign, Math.Sign(vb.CompareTo(va)));

        Assert.Equal(expectedSign > 0, va.GreaterThan(vb));
        Assert.Equal(expectedSign < 0, va.LessThan(vb));
        Assert.Equal(expectedSign >= 0, va.GreaterThanOrEqual(vb));
        Assert.Equal(expectedSign <= 0, va.LessThanOrEqual(vb));
    }

    [Theory]
    [InlineData("2.1.4")]
    [InlineData("v2.1.4")]
    [InlineData("1.0.0-alpha.1+build.7")]
    [InlineData("1.2")]
    [InlineData("10")]
    public void TryParse_AcceptsWellFormed(string input) {
        Assert.True(SemVersion.TryParse(input, out var v));
        Assert.NotNull(v);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-version")]
    [InlineData("1.2.x")]
    [InlineData("..")]
    public void TryParse_RejectsMalformed(string input) {
        Assert.False(SemVersion.TryParse(input, out var v));
        Assert.Null(v);
    }

    [Fact]
    public void TryParse_NullReturnsFalse() {
        Assert.False(SemVersion.TryParse(null, out var v));
        Assert.Null(v);
    }

    [Fact]
    public void Canonical_NormalizesPrefixAndPadding() {
        Assert.Equal("1.2.0", SemVersion.Parse("v1.2").Canonical());
        Assert.Equal("1.0.0", SemVersion.Parse("1").Canonical());
        Assert.Equal("1.2.3-beta", SemVersion.Parse("1.2.3-beta").Canonical());
        Assert.Equal("1.2.3+meta", SemVersion.Parse("1.2.3+meta").Canonical());
    }

    [Theory]
    [InlineData("2.1.4", "2.1.5", true)]
    [InlineData("2.1.4", "2.1.4", false)]
    [InlineData("2.1.4", "2.1.3", false)]
    [InlineData("2.1.4", "2.2.0-rc.1", true)]
    [InlineData("2.2.0-rc.1", "2.2.0", true)]
    public void UpdateAvailableDecision_MatchesGo(string running, string latest, bool updateAvailable) {
        var run = SemVersion.Parse(running);
        var late = SemVersion.Parse(latest);
        Assert.Equal(updateAvailable, run.LessThan(late));
    }
}
