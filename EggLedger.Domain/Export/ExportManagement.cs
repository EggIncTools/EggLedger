using System.Globalization;
using System.Text.RegularExpressions;

namespace EggLedger.Domain.Export;

public sealed class FilePair {
    public string Timestamp { get; set; } = "";
    public string DisplayDate { get; set; } = "";
    public string CsvPath { get; set; } = "";
    public long CsvSize { get; set; }
    public string XlsxPath { get; set; } = "";
    public long XlsxSize { get; set; }
}

public sealed class ExportGroup {
    public string Eid { get; set; } = "";
    public string Nickname { get; set; } = "";
    public string AccountColor { get; set; } = "";
    public List<FilePair> Pairs { get; set; } = [];
}

public readonly record struct ExportFileEntry(string Name, long Size);

public interface IExportFileSystem {
    IReadOnlyList<ExportFileEntry>? ListFiles(string dir);

    long? Size(string path);

    void Delete(string path);
}

public sealed class PhysicalExportFileSystem : IExportFileSystem {
    public IReadOnlyList<ExportFileEntry>? ListFiles(string dir) =>
        Directory.Exists(dir)
            ? [.. Directory.EnumerateFiles(dir)
                .Select(path => new FileInfo(path))
                .Select(info => new ExportFileEntry(info.Name, info.Length))]
            : null;

    public long? Size(string path) => File.Exists(path) ? new FileInfo(path).Length : null;

    public void Delete(string path) {
        if (File.Exists(path)) {
            File.Delete(path);
        }
    }
}

public static class ExportManagement {
    private static readonly Regex ExportFileRe =
        new(@"^(EI\d+)\.(\d{8}_\d{6})\.(csv|xlsx)$", RegexOptions.Compiled);

    public static List<ExportGroup> ListGroups(string exportsDir, IExportFileSystem? fs = null) {
        fs ??= new PhysicalExportFileSystem();
        string missionsDir = Path.Combine(exportsDir, "missions");
        if (fs.ListFiles(missionsDir) is not { } entries) {
            return [];
        }

        var pairsByEid = new Dictionary<string, Dictionary<string, FilePair>>(StringComparer.Ordinal);
        List<string> eidOrder = [];

        foreach (var entry in entries) {
            var m = ExportFileRe.Match(entry.Name);
            if (!m.Success) {
                continue;
            }
            string eid = m.Groups[1].Value;
            string ts = m.Groups[2].Value;
            string ext = m.Groups[3].Value;

            if (!pairsByEid.TryGetValue(eid, out var byTs)) {
                byTs = [with(StringComparer.Ordinal)];
                pairsByEid[eid] = byTs;
                eidOrder.Add(eid);
            }
            if (!byTs.TryGetValue(ts, out var pair)) {
                pair = new FilePair {
                    Timestamp = ts,
                    DisplayDate = FormatExportTimestamp(ts),
                };
                byTs[ts] = pair;
            }

            string fullPath = Path.Combine(missionsDir, entry.Name);
            switch (ext) {
                case "csv":
                    pair.CsvPath = fullPath;
                    pair.CsvSize = entry.Size;
                    break;
                case "xlsx":
                    pair.XlsxPath = fullPath;
                    pair.XlsxSize = entry.Size;
                    break;
            }
        }

        return [.. eidOrder.Select(eid => new ExportGroup {
            Eid = eid,
            Pairs = [.. pairsByEid[eid].Values.OrderByDescending(p => p.Timestamp, StringComparer.Ordinal)],
        })];
    }

    private static string FormatExportTimestamp(string ts) =>
        DateTime.TryParseExact(
            ts,
            "yyyyMMdd_HHmmss",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var t)
            ? t.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
            : ts;

    public static (int DeletedCount, long FreedBytes) PruneForPlayer(
        string exportsDir,
        string playerId,
        int keepCount,
        IExportFileSystem? fs = null) {
        fs ??= new PhysicalExportFileSystem();
        if (keepCount <= 0) {
            return (0, 0);
        }
        var groups = ListGroups(exportsDir, fs);
        List<FilePair> playerPairs = [];
        foreach (var g in groups) {
            if (g.Eid == playerId) {
                playerPairs = g.Pairs;
                break;
            }
        }
        if (playerPairs.Count <= keepCount) {
            return (0, 0);
        }

        int deletedCount = 0;
        long freedBytes = 0;
        var toDelete = playerPairs.GetRange(keepCount, playerPairs.Count - keepCount);
        foreach (var pair in toDelete) {
            foreach (var path in new[] { pair.CsvPath, pair.XlsxPath }) {
                if (string.IsNullOrEmpty(path)) {
                    continue;
                }
                var size = fs.Size(path);
                if (size.HasValue) {
                    freedBytes += size.Value;
                }
                fs.Delete(path);
                deletedCount++;
            }
        }
        return (deletedCount, freedBytes);
    }
}
