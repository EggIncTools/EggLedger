using EggLedger.Web.Settings;

namespace EggLedger.Web.Tests.Settings;

public sealed class ColorPickerMathTests {
    [Theory]
    [InlineData("#6366f1", true)]
    [InlineData("#FFFFFF", true)]
    [InlineData("#abc", false)]
    [InlineData("6366f1", false)]
    [InlineData("#6366f", false)]
    [InlineData("#6366f1f", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidHex_MatchesVueRegex(string? input, bool expected) =>
        Assert.Equal(expected, ColorPickerMath.IsValidHex(input));

    [Fact]
    public void NormalizeToHex_LowersValidHex() =>
        Assert.Equal("#abcdef", ColorPickerMath.NormalizeToHex("#ABCDEF"));

    [Fact]
    public void NormalizeToHex_BadInput_ReturnsFallback() {
        Assert.Equal(ColorPickerMath.Fallback, ColorPickerMath.NormalizeToHex("not-a-color"));
        Assert.Equal(ColorPickerMath.Fallback, ColorPickerMath.NormalizeToHex(null));
    }

    [Fact]
    public void NormalizeToHex_ParsesHslString() {

        Assert.Equal("#ff0000", ColorPickerMath.NormalizeToHex("hsl(0, 100%, 50%)"));

        Assert.Equal("#00ff00", ColorPickerMath.NormalizeToHex("hsl(120, 100%, 50%)"));
    }

    [Fact]
    public void Presets_AreTwentyFourValidHexInVueOrder() {
        Assert.Equal(24, ColorPickerMath.PresetColors.Count);
        Assert.All(ColorPickerMath.PresetColors, c => Assert.True(ColorPickerMath.IsValidHex(c)));
        Assert.Equal("#f43f5e", ColorPickerMath.PresetColors[0]);
        Assert.Equal("#ffffff", ColorPickerMath.PresetColors[23]);
    }

    [Fact]
    public void HslToHex_RoundTripsThroughHexToHslInt() {

        var hsl = ColorPickerMath.HexToHslInt("#ff0000");
        Assert.Equal(0, hsl.H);
        Assert.Equal(100, hsl.S);
        Assert.Equal(50, hsl.L);
        Assert.Equal("#ff0000", ColorPickerMath.HslToHex(hsl.H, hsl.S, hsl.L));
    }

    [Fact]
    public void HexToHslInt_GreyHasZeroSaturation() {
        var hsl = ColorPickerMath.HexToHslInt("#808080");
        Assert.Equal(0, hsl.S);
        Assert.Equal(50, hsl.L);
    }

    [Fact]
    public void WheelHueSaturation_CentreIsZeroSaturation() {
        var (_, s) = ColorPickerMath.WheelHueSaturation(0, 0, 70);
        Assert.Equal(0, s);
    }

    [Fact]
    public void WheelHueSaturation_EdgeIsFullSaturationAndWrapsHue() {

        var (h, s) = ColorPickerMath.WheelHueSaturation(0, -70, 70);
        Assert.Equal(100, s);
        Assert.Equal(0, h);
    }

    [Fact]
    public void DotPosition_CentreForZeroSaturation() {
        var (left, top) = ColorPickerMath.DotPosition(220, 0);
        Assert.Equal(50, left, 3);
        Assert.Equal(50, top, 3);
    }

    [Fact]
    public void DotPosition_OffsetForSaturation() {

        var (left, top) = ColorPickerMath.DotPosition(90, 100);
        Assert.Equal(95, left, 3);
        Assert.Equal(50, top, 3);
    }
}
