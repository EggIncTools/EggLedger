using System.Globalization;
using System.Text;
using EggLedger.Domain.Export.Xlsx;

namespace EggLedger.Domain.Export;

public static class MissionExport {
    private static readonly (string Header, double Width)[] ColumnDefs = [
        ("ID", 40),
        ("Type", 12),
        ("Ship", 26),
        ("Duration Type", 16),
        ("Level", 8),
        ("Launched at", 22),
        ("Returned at", 22),
        ("Duration days", 16),
        ("Capacity", 10),
        ("Target", 26),
    ];

    private static int MaxArtifactCount(IReadOnlyList<Mission> missions) {
        int max = 0;
        foreach (var m in missions) {
            if (m.ArtifactNames.Count > max) {
                max = m.ArtifactNames.Count;
            }
        }
        return max;
    }

    public static List<string> BuildHeader(int maxArtifactCount) {
        return [
            .. ColumnDefs.Select(c => c.Header),
            .. Enumerable.Range(1, maxArtifactCount).Select(i => "Artifact " + i.ToString(CultureInfo.InvariantCulture)),
        ];
    }

    private static List<string> BuildCsvRow(Mission m, int mac) {
        var row = new List<string>(ColumnDefs.Length + mac) {
            m.Id,
            m.TypeName,
            m.ShipName,
            m.DurationTypeName,
            m.Level.ToString(CultureInfo.InvariantCulture),
            m.LaunchedAtStr,
            m.ReturnedAtStr,
            GoFloat.FormatG(m.DurationDays),
            m.Capacity.ToString(CultureInfo.InvariantCulture),
            Mission.GetNamedTarget(m.TargetArtifact),
        };
        for (int i = 0; i < mac; i++) {
            row.Add(i < m.ArtifactNames.Count ? m.ArtifactNames[i] : "");
        }
        return row;
    }

    public static byte[] MissionsToCsvBytes(IReadOnlyList<Mission> missions) {
        int mac = MaxArtifactCount(missions);
        var sb = new StringBuilder();
        WriteCsvRecord(sb, BuildHeader(mac));
        foreach (var m in missions) {
            WriteCsvRecord(sb, BuildCsvRow(m, mac));
        }
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    public static void MissionsToCsv(IReadOnlyList<Mission> missions, string path) =>
        File.WriteAllBytes(path, MissionsToCsvBytes(missions));

    public static byte[] MissionsToXlsxBytes(IReadOnlyList<Mission> missions) {
        int mac = MaxArtifactCount(missions);

        int maxArtifactNameLen = 0;
        foreach (var m in missions) {
            foreach (var name in m.ArtifactNames) {
                if (name.Length > maxArtifactNameLen) {
                    maxArtifactNameLen = name.Length;
                }
            }
        }
        double artifactColWidth = maxArtifactNameLen + 5;

        var colWidths = new double[ColumnDefs.Length + mac];
        for (int i = 0; i < ColumnDefs.Length; i++) {
            colWidths[i] = ColumnDefs[i].Width;
        }
        for (int i = 0; i < mac; i++) {
            colWidths[ColumnDefs.Length + i] = artifactColWidth;
        }

        using var ms = new MemoryStream();
        using (var w = XlsxWriter.New(ms)) {
            w.SetColWidths(colWidths);

            w.WriteRow([.. BuildHeader(mac).Select(XlsxCell.String)]);

            foreach (var m in missions) {
                var row = new List<XlsxCell>(ColumnDefs.Length + mac) {
                    XlsxCell.String(m.Id),
                    XlsxCell.String(m.TypeName),
                    XlsxCell.String(m.ShipName),
                    XlsxCell.String(m.DurationTypeName),
                    XlsxCell.Number(m.Level),
                    XlsxCell.Datetime(m.LaunchedAt),
                    XlsxCell.Datetime(m.ReturnedAt),
                    XlsxCell.Number(m.DurationDays),
                    XlsxCell.Number(m.Capacity),
                    XlsxCell.String(Mission.GetNamedTarget(m.TargetArtifact)),
                };
                for (int i = 0; i < mac; i++) {
                    row.Add(XlsxCell.String(i < m.ArtifactNames.Count ? m.ArtifactNames[i] : ""));
                }
                w.WriteRow(row);
            }
            w.Close();
        }
        return ms.ToArray();
    }

    public static void MissionsToXlsx(IReadOnlyList<Mission> missions, string path) =>
        File.WriteAllBytes(path, MissionsToXlsxBytes(missions));

    public static string FilenameWithoutExt(string f) => Path.GetFileNameWithoutExtension(f);

    private static void WriteCsvRecord(StringBuilder sb, List<string> fields) {
        for (int i = 0; i < fields.Count; i++) {
            if (i > 0) {
                sb.Append(',');
            }
            sb.Append(QuoteCsvField(fields[i]));
        }
        sb.Append("\r\n");
    }

    private static string QuoteCsvField(string field) {
        if (!NeedsQuoting(field)) {
            return field;
        }
        var sb = new StringBuilder(field.Length + 2);
        sb.Append('"');
        foreach (char c in field) {
            if (c == '"') {
                sb.Append("\"\"");
            } else {
                sb.Append(c);
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    private static bool NeedsQuoting(string field) =>
        field.Length != 0 && field.IndexOfAny(['"', ',', '\r', '\n']) >= 0;
}
