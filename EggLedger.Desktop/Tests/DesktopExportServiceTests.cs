using EggLedger.Desktop.Export;
using EggLedger.Desktop.Storage;
using EggLedger.Domain.Export;
using EggLedger.Domain.MissionQuery;

namespace EggLedger.Desktop.Tests;

public sealed class DesktopExportServiceTests {

    private sealed class FakeExportFileSystem : IExportFileSystem {
        private readonly Dictionary<string, long> _files = [with(StringComparer.Ordinal)];

        public void AddFile(string dir, string name, long size) =>
            _files[Path.Combine(dir, name)] = size;

        public bool Exists(string path) => _files.ContainsKey(path);

        public IReadOnlyList<ExportFileEntry>? ListFiles(string dir) {
            var prefix = dir.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var entries = new List<ExportFileEntry>();
            bool dirSeen = false;
            foreach (var (path, size) in _files) {
                if (!path.StartsWith(prefix, StringComparison.Ordinal)) {
                    continue;
                }
                var rest = path[prefix.Length..];
                if (rest.Contains(Path.DirectorySeparatorChar)) {
                    continue;
                }
                dirSeen = true;
                entries.Add(new ExportFileEntry(rest, size));
            }
            return dirSeen ? entries : null;
        }

        public long? Size(string path) => _files.TryGetValue(path, out var s) ? s : null;

        public void Delete(string path) => _files.Remove(path);
    }

    private static string MissionsDir(string root) =>
        Path.Combine(StoragePaths.ResolveExportsDir(root), "missions");

    [Fact]
    public async Task ListAsync_ReturnsGroupsNewestFirst() {
        const string root = "root";
        var fs = new FakeExportFileSystem();
        var md = MissionsDir(root);
        fs.AddFile(md, "EI123.20240311_221737.csv", 100);
        fs.AddFile(md, "EI123.20240311_221737.xlsx", 200);
        fs.AddFile(md, "EI123.20240312_100000.csv", 150);
        fs.AddFile(md, "EI456.20240311_221737.csv", 300);

        var svc = new DesktopExportService(root, fs);
        var groups = await svc.ListAsync();

        Assert.Equal(2, groups.Count);
        var ei123 = groups.Single(g => g.Eid == "EI123");
        Assert.Equal(2, ei123.Pairs.Count);
        Assert.Equal("20240312_100000", ei123.Pairs[0].Timestamp);
        Assert.Equal("20240311_221737", ei123.Pairs[1].Timestamp);
    }

    [Fact]
    public async Task ListAsync_EnrichesFromAccountLookup() {
        const string root = "root";
        var fs = new FakeExportFileSystem();
        fs.AddFile(MissionsDir(root), "EI123.20240311_221737.csv", 100);

        var accounts = new List<AccountInfo> {
            new() { Id = "EI123", Nickname = "Coop", AccountColor = "#abc" },
        };
        var svc = new DesktopExportService(root, fs, () => Task.FromResult<IReadOnlyList<AccountInfo>>(accounts));
        var groups = await svc.ListAsync();

        var g = Assert.Single(groups);
        Assert.Equal("Coop", g.Nickname);
        Assert.Equal("#abc", g.AccountColor);
    }

    [Fact]
    public async Task PruneAsync_DeletesOldestBeyondKeepAcrossGroups() {
        const string root = "root";
        var fs = new FakeExportFileSystem();
        var md = MissionsDir(root);
        foreach (var f in new[] {
            "EI123.20240310_000000.csv", "EI123.20240310_000000.xlsx",
            "EI123.20240311_000000.csv", "EI123.20240311_000000.xlsx",
            "EI123.20240312_000000.csv", "EI123.20240312_000000.xlsx",
            "EI456.20240310_000000.csv", "EI456.20240311_000000.csv",
        }) {
            fs.AddFile(md, f, 4);
        }

        var svc = new DesktopExportService(root, fs);
        var (deleted, freed) = await svc.PruneAsync(1);

        Assert.Equal(5, deleted);
        Assert.Equal(20, freed);
        Assert.False(fs.Exists(Path.Combine(md, "EI123.20240310_000000.csv")));
        Assert.True(fs.Exists(Path.Combine(md, "EI123.20240312_000000.csv")));
        Assert.False(fs.Exists(Path.Combine(md, "EI456.20240310_000000.csv")));
        Assert.True(fs.Exists(Path.Combine(md, "EI456.20240311_000000.csv")));
    }

    [Fact]
    public async Task PruneAsync_KeepCountZeroIsNoOp() {
        const string root = "root";
        var fs = new FakeExportFileSystem();
        fs.AddFile(MissionsDir(root), "EI123.20240311_221737.csv", 10);

        var svc = new DesktopExportService(root, fs);
        var (deleted, freed) = await svc.PruneAsync(0);

        Assert.Equal(0, deleted);
        Assert.Equal(0, freed);
    }

    [Fact]
    public async Task DeleteAsync_RemovesGivenPaths_IgnoresMissing() {
        const string root = "root";
        var fs = new FakeExportFileSystem();
        var md = MissionsDir(root);
        fs.AddFile(md, "EI123.20240311_221737.csv", 10);
        fs.AddFile(md, "EI123.20240311_221737.xlsx", 20);
        var csv = Path.Combine(md, "EI123.20240311_221737.csv");
        var missing = Path.Combine(md, "EI999.20990101_000000.csv");

        var svc = new DesktopExportService(root, fs);
        await svc.DeleteAsync(new[] { csv, missing });

        Assert.False(fs.Exists(csv));
        Assert.True(fs.Exists(Path.Combine(md, "EI123.20240311_221737.xlsx")));
    }
}
