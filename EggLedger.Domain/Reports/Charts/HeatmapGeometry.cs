using System.Globalization;

namespace EggLedger.Domain.Reports.Charts;

public readonly record struct HeatmapCellStyle(string BackgroundColor, string Color);

public static class HeatmapGeometry {
    public static bool IsPct(string? normalizeBy) =>
        normalizeBy is "row_pct" or "col_pct" or "global_pct";

    public static string SafeColor(string? color, string fallback) =>
        color is not null && color.StartsWith('#') && color.Length == 7 ? color : fallback;

    public static (int R, int G, int B) HexToRgb(string hex) => (
        int.Parse(hex.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
        int.Parse(hex.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
        int.Parse(hex.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));

    public static double RelativeLuminance(int r, int g, int b) {
        static double ToLinear(int c) {
            double s = c / 255.0;
            return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * ToLinear(r) + 0.7152 * ToLinear(g) + 0.0722 * ToLinear(b);
    }

    public static double CellIntensity(
        double value,
        double globalMax,
        string? normalizeBy,
        double? colorValue) {
        if (colorValue is double cv) {
            return Math.Max(0.12, cv > 0 ? Math.Min(cv / 2, 1) : 0);
        }
        if (IsPct(normalizeBy)) {
            return Math.Max(0.12, value / 100);
        }
        if (normalizeBy == "ratio") {
            return Math.Max(0.12, Math.Min(value / 2, 1));
        }
        return Math.Max(0.12, globalMax > 0 ? value / globalMax : 0);
    }

    public static bool IsBelowThreshold(int? missionCount, int minSampleSize) =>
        minSampleSize > 0 && missionCount is { } count && count < minSampleSize;

    public static HeatmapCellStyle CellStyle(
        double value,
        double globalMax,
        string baseColor,
        string unfilledColor,
        string? normalizeBy,
        double? colorValue,
        bool belowThreshold) {
        string safeBase = SafeColor(baseColor, "#6366f1");
        string safeUnfilled = SafeColor(unfilledColor, "#1f2937");

        if (belowThreshold || value == 0) {
            var (ur, ug, ub) = HexToRgb(safeUnfilled);
            return new HeatmapCellStyle(
                safeUnfilled,
                RelativeLuminance(ur, ug, ub) > 0.179 ? "#1f2937" : "#6b7280");
        }

        double intensity = CellIntensity(value, globalMax, normalizeBy, colorValue);
        var (fr, fg, fb) = HexToRgb(safeBase);
        var (br, bg, bb) = HexToRgb(safeUnfilled);
        int r = (int)Math.Round(br + (fr - br) * intensity);
        int g = (int)Math.Round(bg + (fg - bg) * intensity);
        int b = (int)Math.Round(bb + (fb - bb) * intensity);
        return new HeatmapCellStyle(
            $"rgb({r}, {g}, {b})",
            RelativeLuminance(r, g, b) > 0.179 ? "#1f2937" : "#f3f4f6");
    }

    public static string DisplayValue(double value, string? normalizeBy, bool belowThreshold) {
        if (belowThreshold) {
            return "-";
        }
        if (normalizeBy == "ratio") {
            return value == 0 ? "-" : "x" + value.ToString("0.00", CultureInfo.InvariantCulture);
        }
        if (value == 0) {
            return "0";
        }
        if (IsPct(normalizeBy)) {
            return value.ToString("0.0", CultureInfo.InvariantCulture) + "%";
        }
        return value % 1 == 0
            ? ((long)value).ToString(CultureInfo.InvariantCulture)
            : value.ToString("0.00", CultureInfo.InvariantCulture);
    }

    public static string SecondaryDisplayValue(double value, string? normalizeBy, bool belowThreshold) {
        if (belowThreshold) {
            return "";
        }
        if (value == 0) {
            return "0";
        }
        if (IsPct(normalizeBy)) {
            return value.ToString("0.0", CultureInfo.InvariantCulture) + "%";
        }
        return value % 1 == 0
            ? ((long)value).ToString(CultureInfo.InvariantCulture)
            : value.ToString("0.00", CultureInfo.InvariantCulture);
    }
}
