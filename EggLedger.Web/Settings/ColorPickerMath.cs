using System.Globalization;
using System.Text.RegularExpressions;
using EggLedger.Domain.Reports.Charts;

namespace EggLedger.Web.Settings;

public static partial class ColorPickerMath {
    public static readonly IReadOnlyList<string> PresetColors = [
        "#f43f5e", "#ef4444", "#f97316", "#f59e0b",
        "#22c55e", "#10b981", "#14b8a6", "#06b6d4",
        "#3b82f6", "#6366f1", "#8b5cf6", "#a855f7",
        "#d946ef", "#ec4899", "#64748b", "#94a3b8",
        "#e2e8f0", "#fbbf24", "#fb923c", "#c084fc",
        "#60a5fa", "#34d399", "#f9a8d4", "#ffffff",
    ];

    public const string Fallback = "#6366f1";
    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexRegexGen();
    [GeneratedRegex(@"hsl\(\s*([\d.]+)\s*,\s*([\d.]+)%\s*,\s*([\d.]+)%\s*\)")]
    private static partial Regex HslRegexGen();

    public static bool IsValidHex(string? value) =>
        value is not null && HexRegexGen().IsMatch(value);

    public readonly record struct HslInt(int H, int S, int L);

    public static string NormalizeToHex(string? value) {
        if (value is null) {
            return Fallback;
        }
        if (HexRegexGen().IsMatch(value)) {
            return value.ToLowerInvariant();
        }
        var m = HslRegexGen().Match(value);
        if (m.Success) {
            double h = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            double s = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            double l = double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
            return HslToHex(h, s, l);
        }
        return Fallback;
    }

    public static string HslToHex(double h, double s, double l) =>
        SliceColors.HslToHex(h, s / 100.0, l / 100.0);

    public static HslInt HexToHslInt(string hex) {
        var (h, s, l) = SliceColors.HexToHsl(hex);
        return new HslInt(
            (int)Math.Round(h),
            (int)Math.Round(s * 100),
            (int)Math.Round(l * 100));
    }

    public static (int H, int S) WheelHueSaturation(double dx, double dy, double radius) {
        double rawAngle = Math.Atan2(dy, dx) * (180 / Math.PI) + 90;
        double hue = Mod(rawAngle, 360);
        double dist = Math.Sqrt(dx * dx + dy * dy);
        double ratio = radius <= 0 ? 0 : Math.Min(dist / radius, 1);
        int sat = (int)Math.Round(ratio * 100);
        return ((int)Math.Round(hue), sat);
    }

    public static (double LeftPct, double TopPct) DotPosition(int hue, int saturation) {
        double rad = (hue - 90) * Math.PI / 180;
        double r = saturation / 100.0 * 45;
        return (50 + r * Math.Cos(rad), 50 + r * Math.Sin(rad));
    }

    private static double Mod(double a, double n) {
        double r = a % n;
        return r < 0 ? r + n : r;
    }
}
