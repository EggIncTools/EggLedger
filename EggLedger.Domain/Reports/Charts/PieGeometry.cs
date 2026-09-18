using System.Globalization;

namespace EggLedger.Domain.Reports.Charts;

public readonly record struct PieItem(string Label, double Value);

public readonly record struct PieSlice(string Label, double Value, double Pct, string Path, string Color);

public static class PieGeometry {
    public const int MaxSegments = 10;

    public static List<PieItem> BuildItems(IReadOnlyList<string> labels, IReadOnlyList<double> values) {
        double total = values.Sum();
        if (total == 0) {
            return [];
        }

        List<PieItem> items = [.. labels.Select((l, i) => new PieItem(l, i < values.Count ? values[i] : 0))];

        if (items.Count > MaxSegments) {
            var sorted = items.OrderByDescending(x => x.Value).ToList();
            var kept = sorted.Take(MaxSegments - 1).ToList();
            double other = sorted.Skip(MaxSegments - 1).Sum(x => x.Value);
            kept.Add(new PieItem("Other", other));
            return kept;
        }
        return items;
    }

    public static string SlicePath(double cx, double cy, double r, double startAngle, double endAngle) {
        double sx = cx + Math.Cos(startAngle) * r;
        double sy = cy + Math.Sin(startAngle) * r;
        double ex = cx + Math.Cos(endAngle) * r;
        double ey = cy + Math.Sin(endAngle) * r;
        int large = endAngle - startAngle > Math.PI ? 1 : 0;
        return string.Format(
            CultureInfo.InvariantCulture,
            "M {0} {1} L {2} {3} A {4} {5} 0 {6} 1 {7} {8} Z",
            cx, cy, sx, sy, r, r, large, ex, ey);
    }

    public static List<PieSlice> BuildSlices(
        IReadOnlyList<string> labels,
        IReadOnlyList<double> values,
        double cx,
        double cy,
        double r,
        string baseColor,
        IReadOnlyDictionary<string, string> labelColors) {
        var items = BuildItems(labels, values);
        var slices = new List<PieSlice>(items.Count);
        if (items.Count == 0) {
            return slices;
        }

        double total = items.Sum(i => i.Value);
        var autoColors = SliceColors.AutoSliceColors(baseColor, items.Count);
        double angleOffset = -Math.PI / 2;
        for (int i = 0; i < items.Count; i++) {
            var item = items[i];
            double sweep = total == 0 ? 0 : item.Value / total * 2 * Math.PI;
            double startAngle = angleOffset;
            double endAngle = angleOffset + sweep;
            angleOffset = endAngle;

            string color = labelColors.TryGetValue(item.Label, out var c) && !string.IsNullOrEmpty(c)
                ? c
                : autoColors[i];
            double pct = total == 0 ? 0 : item.Value / total * 100;
            slices.Add(new PieSlice(
                item.Label,
                item.Value,
                pct,
                SlicePath(cx, cy, r, startAngle, endAngle),
                color));
        }
        return slices;
    }
}
