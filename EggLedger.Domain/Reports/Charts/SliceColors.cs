using System.Globalization;
using System.Text.Json;

namespace EggLedger.Domain.Reports.Charts;

public static class SliceColors {
    private const string DefaultBase = "#6366f1";

    public static (double H, double S, double L) HexToHsl(string hex) {
        if (!IsHexColor(hex)) {
            hex = DefaultBase;
        }
        double rv = ParseByte(hex, 1) / 255.0;
        double gv = ParseByte(hex, 3) / 255.0;
        double bv = ParseByte(hex, 5) / 255.0;
        double max = Math.Max(rv, Math.Max(gv, bv));
        double min = Math.Min(rv, Math.Min(gv, bv));
        double l = (max + min) / 2;
        double d = max - min;
        double s = d == 0 ? 0 : d / (1 - Math.Abs(2 * l - 1));
        double h = 0;
        if (d != 0) {
            h = max == rv ? Mod((gv - bv) / d + 6, 6) : max == gv ? (bv - rv) / d + 2 : (rv - gv) / d + 4;
            h *= 60;
        }
        return (h, s, l);
    }

    public static string HslToHex(double h, double s, double l) {
        double a = s * Math.Min(l, 1 - l);
        string F(int n) {
            double k = Mod(n + h / 30, 12);
            double color = l - a * Math.Max(Math.Min(Math.Min(k - 3, 9 - k), 1), -1);
            int v = (int)Math.Round(255 * color);
            return v.ToString("x2", CultureInfo.InvariantCulture);
        }
        return $"#{F(0)}{F(8)}{F(4)}";
    }

    public static IReadOnlyList<string> AutoSliceColors(string baseColor, int count) {
        var (h, s, l) = HexToHsl(baseColor);
        return Enumerable.Range(0, Math.Max(count, 0)).Select(i => HslToHex(Mod(h + (double)i * 360 / count, 360), s, l)).ToList();
    }

    public static Dictionary<string, string> ParseLabelColors(string? raw) {
        if (string.IsNullOrEmpty(raw)) {
            return [with(StringComparer.Ordinal)];
        }
        try {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(raw);
            return parsed ?? [with(StringComparer.Ordinal)];
        } catch (JsonException) {
            return [with(StringComparer.Ordinal)];
        }
    }

    public static string GetLabelColor(
        string label,
        string baseColor,
        IReadOnlyList<string> chartLabels,
        IReadOnlyDictionary<string, string> labelColorsMap) {
        if (labelColorsMap.TryGetValue(label, out var overridden) && !string.IsNullOrEmpty(overridden)) {
            return overridden;
        }
        int idx = IndexOf(chartLabels, label);
        var colors = AutoSliceColors(baseColor, chartLabels.Count);
        if (idx >= 0 && idx < colors.Count) {
            return colors[idx];
        }
        return baseColor;
    }

    private static int IndexOf(IReadOnlyList<string> list, string value) {
        for (int i = 0; i < list.Count; i++) {
            if (list[i] == value) {
                return i;
            }
        }
        return -1;
    }

    private static bool IsHexColor(string? hex) {
        if (hex is null || hex.Length != 7 || hex[0] != '#') {
            return false;
        }
        for (int i = 1; i < 7; i++) {
            if (!Uri.IsHexDigit(hex[i])) {
                return false;
            }
        }
        return true;
    }

    private static int ParseByte(string hex, int start) =>
        int.Parse(hex.AsSpan(start, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);


    private static double Mod(double a, double n) {
        double r = a % n;
        return r < 0 ? r + n : r;
    }
}
