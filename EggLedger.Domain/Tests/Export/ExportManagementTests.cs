using EggLedger.Domain.Export;

namespace EggLedger.Domain.Tests.Export;

public class ExportManagementTests {
    private static string MissionsDir(string root) => Path.Combine(root, "missions");

    [Fact]
    public void ListGroups_MissingDir() {
        var fs = new InMemoryExportFileSystem();
        var groups = ExportManagement.ListGroups("root", fs);
        Assert.Empty(groups);
    }

    [Fact]
    public void ListGroups_ParsesAndGroups() {
        const string root = "root";
        var md = MissionsDir(root);
        var fs = new InMemoryExportFileSystem();
        fs.AddFile(md, "EI123.20240311_221737.csv", 100);
        fs.AddFile(md, "EI123.20240311_221737.xlsx", 200);
        fs.AddFile(md, "EI123.20240312_100000.csv", 150);
        fs.AddFile(md, "EI456.20240311_221737.csv", 300);
        fs.AddFile(md, "ignored.txt", 10);

        var groups = ExportManagement.ListGroups(root, fs);
        Assert.Equal(2, groups.Count);

        var ei123 = groups.Single(g => g.Eid == "EI123");
        Assert.Equal(2, ei123.Pairs.Count);
        Assert.Equal("20240312_100000", ei123.Pairs[0].Timestamp);
        Assert.Equal(100, ei123.Pairs[1].CsvSize);
        Assert.Equal(200, ei123.Pairs[1].XlsxSize);
    }

    [Fact]
    public void ListGroups_PartialPair() {
        const string root = "root";
        var fs = new InMemoryExportFileSystem();
        fs.AddFile(MissionsDir(root), "EI123.20240311_221737.csv", 50);

        var groups = ExportManagement.ListGroups(root, fs);
        Assert.Single(groups);
        Assert.Single(groups[0].Pairs);
        Assert.Equal("", groups[0].Pairs[0].XlsxPath);
    }

    [Fact]
    public void PruneForPlayer_DeletesOldest() {
        const string root = "root";
        var md = MissionsDir(root);
        var fs = new InMemoryExportFileSystem();
        foreach (var f in new[]
        {
            "EI123.20240310_000000.csv", "EI123.20240310_000000.xlsx",
            "EI123.20240311_000000.csv", "EI123.20240311_000000.xlsx",
            "EI123.20240312_000000.csv", "EI123.20240312_000000.xlsx",
        }) {
            fs.AddFile(md, f, 4);
        }

        var (deleted, freed) = ExportManagement.PruneForPlayer(root, "EI123", 2, fs);
        Assert.Equal(2, deleted);
        Assert.Equal(8, freed);
        Assert.False(fs.Exists(Path.Combine(md, "EI123.20240310_000000.csv")));
        Assert.True(fs.Exists(Path.Combine(md, "EI123.20240312_000000.csv")));
    }

    [Fact]
    public void PruneForPlayer_NoOp() {
        var fs = new InMemoryExportFileSystem();
        var (deleted, freed) = ExportManagement.PruneForPlayer("root", "EI123", 0, fs);
        Assert.Equal(0, deleted);
        Assert.Equal(0, freed);
    }

    [Fact]
    public void PruneForPlayer_BelowLimit() {
        const string root = "root";
        var fs = new InMemoryExportFileSystem();
        fs.AddFile(MissionsDir(root), "EI123.20240311_221737.csv", 10);

        var (deleted, _) = ExportManagement.PruneForPlayer(root, "EI123", 5, fs);
        Assert.Equal(0, deleted);
    }
}
