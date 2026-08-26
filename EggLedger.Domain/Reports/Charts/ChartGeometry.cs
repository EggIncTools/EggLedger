using System.Globalization;
using System.Text.RegularExpressions;

namespace EggLedger.Domain.Reports.Charts;

public readonly record struct ChartPoint(double X, double Y, string Label, double Value);

public readonly record struct YTick(double Y, string Label);

public static partial class ChartGeometry {
    public const double PadLeft = 36;
    public const double PadRight = 8;
    public const double PadTop = 10;
    public const double PadBottom = 60;
    public const int MaxLabels = 12;
    private static readonly string[] Months =
    [
        "Jan", "Feb", "Mar", "Apr", "May", "Jun",
        "Jul", "Aug", "Sep", "Oct", "Nov", "Dec",
    ];

    public static List<double> Values(IReadOnlyList<long> values, IReadOnlyList<double> floatValues, bool isFloat) =>
        isFloat
            ? [.. floatValues]
            : [.. values.Select(v => (double)v)];

    public static List<ChartPoint> Points(IReadOnlyList<double> values, IReadOnlyList<string> labels, double w, double h) {
        if (values.Count < 2) {
            return [];
        }
        double max = Math.Max(values.Max(), 1);
        double xStep = (w - PadLeft - PadRight) / (values.Count - 1);
        double yRange = h - PadTop - PadBottom;
        var points = new List<ChartPoint>(values.Count);
        for (int i = 0; i < values.Count; i++) {
            double v = values[i];
            points.Add(new ChartPoint(
                PadLeft + i * xStep,
                PadTop + yRange * (1 - v / max),
                i < labels.Count ? labels[i] : "",
                v));
        }
        return points;
    }

    public static string PolylinePoints(IReadOnlyList<ChartPoint> points) =>
        string.Join(" ", points.Select(p => Fmt(p.X) + "," + Fmt(p.Y)));

    public static string AreaPath(IReadOnlyList<ChartPoint> points, double h) {
        if (points.Count < 2) {
            return "";
        }
        double baseline = PadTop + (h - PadTop - PadBottom);
        var line = string.Join(" ", points.Select((p, i) => (i == 0 ? "M" : "L") + Fmt(p.X) + "," + Fmt(p.Y)));
        var last = points[^1];
        var first = points[0];
        return $"{line} L{Fmt(last.X)},{Fmt(baseline)} L{Fmt(first.X)},{Fmt(baseline)} Z";
    }

    public static List<ChartPoint> ThinLabels(IReadOnlyList<ChartPoint> points) {
        if (points.Count <= MaxLabels) {
            return [.. points];
        }
        int step = (int)Math.Ceiling((double)points.Count / MaxLabels);
        var result = new List<ChartPoint>();
        for (int i = 0; i < points.Count; i++) {
            if (i % step == 0 || i == points.Count - 1) {
                result.Add(points[i]);
            }
        }
        return result;
    }

    public static List<YTick> YTicks(double max, double h, bool isFloat) {
        if (max == 0) {
            return [];
        }
        double yRange = h - PadTop - PadBottom;
        double[] fracs = [0.33, 0.67, 1.0];
        var ticks = new List<YTick>(3);
        foreach (var frac in fracs) {
            double val = max * frac;
            double y = PadTop + yRange * (1 - frac);
            string label = isFloat
                ? val.ToString("0.0", CultureInfo.InvariantCulture)
                : Math.Round(val).ToString(CultureInfo.InvariantCulture);
            ticks.Add(new YTick(y, label));
        }
        return ticks;
    }

    public static string FormatXLabel(string s) {
        var ym = YearMonthRegex().Match(s);
        if (ym.Success) {
            int n = int.Parse(ym.Groups[2].Value, CultureInfo.InvariantCulture);
            string yr = ym.Groups[1].Value[2..];
            if (n is >= 1 and <= 12) {
                return $"{Months[n - 1]} '{yr}";
            }
            return $"W{ym.Groups[2].Value} '{yr}";
        }
        var ymd = YearMonthDayRegex().Match(s);
        if (ymd.Success) {
            int m = int.Parse(ymd.Groups[2].Value, CultureInfo.InvariantCulture);
            int d = int.Parse(ymd.Groups[3].Value, CultureInfo.InvariantCulture);
            return $"{d} {Months[m - 1]}";
        }
        return s.Length > 10 ? s[..9] + "..." : s;
    }

    public static List<string> SeriesColors(
        IReadOnlyList<string> colLabels,
        string baseColor,
        IReadOnlyDictionary<string, string> labelColors) =>
        SeriesColors(colLabels, baseColor, labelColors, colLabels.Count, null);

    public static List<string> SeriesColors(
        IReadOnlyList<string> colLabels,
        string baseColor,
        IReadOnlyDictionary<string, string> labelColors,
        int hueDenominator,
        (string Label, string Color)? otherSentinel) {
        int n = colLabels.Count;
        if (n == 0) {
            return [];
        }
        string baseHex = baseColor.StartsWith('#') && baseColor.Length == 7 ? baseColor : "#6366f1";
        var (h, s, _) = SliceColors.HexToHsl(baseHex);

        double sPct = s * 100;
        double denom = hueDenominator <= 0 ? n : hueDenominator;
        var result = new List<string>(n);
        for (int i = 0; i < n; i++) {
            string label = colLabels[i];
            if (otherSentinel is { } sentinel && label == sentinel.Label) {
                result.Add(sentinel.Color);
                continue;
            }
            if (labelColors.TryGetValue(label, out var c) && !string.IsNullOrEmpty(c)) {
                result.Add(c);
                continue;
            }
            double hue = (h + 360.0 / denom * i) % 360;
            result.Add(string.Format(
                CultureInfo.InvariantCulture,
                "hsl({0:0}, {1:0}%, 55%)",
                hue,
                sPct));
        }
        return result;
    }

    public static List<double> SeriesValues(IReadOnlyList<double> matrix, int rowCount, int colCount, int seriesIdx) {
        var result = new List<double>(rowCount);
        for (int r = 0; r < rowCount; r++) {
            int flat = r * colCount + seriesIdx;
            result.Add(flat < matrix.Count ? matrix[flat] : 0);
        }
        return result;
    }

    public static string EscapeText(string s) =>
        s.Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&#39;");

    private static string Fmt(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^(\d{4})-(\d{2})$")]
    private static partial Regex YearMonthRegex();

    [GeneratedRegex(@"^(\d{4})-(\d{2})-(\d{2})$")]
    private static partial Regex YearMonthDayRegex();
}
