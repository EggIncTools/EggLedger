using EggLedger.Desktop.Storage;
using EggLedger.Web.Platform;

namespace EggLedger.Desktop.Tests;

public sealed class DesktopStorageServiceTests : IDisposable {
    private readonly TempDir _temp = new("egglg-store");

    private string Root => _temp.Path;

    public void Dispose() => _temp.Dispose();

    private sealed class FakePlatform : IPlatformCapabilities {
        public int RestartCalls { get; private set; }
        public bool IsDesktop => true;
        public Task RestartAppAsync() {
            RestartCalls++;
            return Task.CompletedTask;
        }
        public Task OpenFileAsync(string path) => Task.CompletedTask;
        public Task OpenUrlAsync(string url) => Task.CompletedTask;
        public Task OpenFileInFolderAsync(string path) => Task.CompletedTask;
        public Task<string?> ChooseSaveFilePathAsync(string defaultName) => Task.FromResult<string?>(null);
        public Task<(int w, int h)> GetWindowSizeAsync() => Task.FromResult((0, 0));
        public Task<string?> ChooseFolderAsync() => Task.FromResult<string?>(null);
        public Task SetFolderHiddenAsync(string path, bool hidden) => Task.CompletedTask;
        public string DataRootDir => "";
    }

    private string SourceRoot() => Path.Combine(Root, "src");
    private string Dest() => Path.Combine(Root, "dst");

    private void SeedPart(string subDir, string fileName, string content) {
        var dir = Path.Combine(SourceRoot(), subDir);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, fileName), content);
    }

    private DesktopStorageService NewService(FakePlatform platform, List<string>? bootstrapWrites = null) =>
        new(SourceRoot(), platform, dest => bootstrapWrites?.Add(dest));

    [Fact]
    public async Task BackupAsync_CopiesOnlySelectedParts() {
        SeedPart("internal", "ledger.db", "db-bytes");
        SeedPart("exports", "report.csv", "csv-bytes");
        SeedPart("logs", "app.log", "log-bytes");
        var dest = Dest();
        var service = NewService(new FakePlatform());

        await service.BackupAsync(dest, db: true, exports: true, logs: false);

        Assert.Equal("db-bytes", File.ReadAllText(Path.Combine(dest, "internal", "ledger.db")));
        Assert.Equal("csv-bytes", File.ReadAllText(Path.Combine(dest, "exports", "report.csv")));
        Assert.False(Directory.Exists(Path.Combine(dest, "logs")));
    }

    [Fact]
    public async Task BackupAsync_SkipsMissingSourcePart() {
        SeedPart("internal", "ledger.db", "db-bytes");
        var dest = Dest();
        var service = NewService(new FakePlatform());

        await service.BackupAsync(dest, db: true, exports: true, logs: true);

        Assert.True(File.Exists(Path.Combine(dest, "internal", "ledger.db")));
        Assert.False(Directory.Exists(Path.Combine(dest, "exports")));
        Assert.False(Directory.Exists(Path.Combine(dest, "logs")));
    }

    [Fact]
    public async Task BackupAsync_LeavesSourceUntouched() {
        SeedPart("internal", "ledger.db", "db-bytes");
        var dest = Dest();
        var service = NewService(new FakePlatform());

        await service.BackupAsync(dest, db: true, exports: false, logs: false);

        Assert.Equal("db-bytes", File.ReadAllText(Path.Combine(SourceRoot(), "internal", "ledger.db")));
    }

    [Fact]
    public async Task MoveAsync_CopiesAllParts_WritesBootstrap_AndRestarts() {
        SeedPart("internal", "ledger.db", "db-bytes");
        SeedPart("exports", "report.csv", "csv-bytes");
        SeedPart("logs", "app.log", "log-bytes");
        var dest = Dest();
        var platform = new FakePlatform();
        var bootstrapWrites = new List<string>();
        var service = NewService(platform, bootstrapWrites);

        await service.MoveAsync(dest);

        Assert.Equal("db-bytes", File.ReadAllText(Path.Combine(dest, "internal", "ledger.db")));
        Assert.Equal("csv-bytes", File.ReadAllText(Path.Combine(dest, "exports", "report.csv")));
        Assert.Equal("log-bytes", File.ReadAllText(Path.Combine(dest, "logs", "app.log")));
        Assert.Equal(dest, Assert.Single(bootstrapWrites));
        Assert.Equal(1, platform.RestartCalls);
    }

    [Fact]
    public async Task MoveAsync_ThrowsWhenDestIsCurrentRoot() {
        var platform = new FakePlatform();
        var bootstrapWrites = new List<string>();
        var service = NewService(platform, bootstrapWrites);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.MoveAsync(service.GetDataRootDir()));

        Assert.Empty(bootstrapWrites);
        Assert.Equal(0, platform.RestartCalls);
    }
}
