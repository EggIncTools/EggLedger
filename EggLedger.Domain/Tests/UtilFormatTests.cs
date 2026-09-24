using EggLedger.Domain.Util;

namespace EggLedger.Domain.Tests;

public class UtilFormatTests {
    [Fact]
    public void Addendum_Zero() => Assert.Equal("", Format.Addendum(0));

    [Fact]
    public void Addendum_K() => Assert.Equal("K", Format.Addendum(1));

    [Fact]
    public void Addendum_M() => Assert.Equal("M", Format.Addendum(2));

    [Fact]
    public void Addendum_T() => Assert.Equal("T", Format.Addendum(4));

    [Fact]
    public void Addendum_AboveCap() => Assert.Equal("!", Format.Addendum(21));

    [Fact]
    public void AbbreviateFloat_BelowThousand() => Assert.Equal("999", Format.AbbreviateFloat(999));

    [Fact]
    public void AbbreviateFloat_Kilo() => Assert.Equal("1.23K", Format.AbbreviateFloat(1234));

    [Fact]
    public void AbbreviateFloat_Billion() => Assert.Equal("1.23B", Format.AbbreviateFloat(1_234_567_890));

    [Fact]
    public void AbbreviateFloat_TenBillion() => Assert.Equal("10.0B", Format.AbbreviateFloat(10_000_000_000));

    [Fact]
    public void AbbreviateFloat_HundredBillion() => Assert.Equal("100B", Format.AbbreviateFloat(100_000_000_000));
}
