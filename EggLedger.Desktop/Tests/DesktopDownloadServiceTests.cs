using System.IO.Compression;
using EggLedger.Desktop.Export;
using EggLedger.Domain.Export;
using EggLedger.Web.Platform;

namespace EggLedger.Desktop.Tests;

public sealed class DesktopDownloadServiceTests : IDisposable {
    private readonly TempDir _temp = new("egglg-dl");

    private string TempPath => _temp.Path;

    public void Dispose() => _temp.Dispose();

    private sealed class FakePlatform(string? saveResult) : IPlatformCapabilities {
        public string? SaveResult { get; } = saveResult;
        public string? LastDefaultName { get; private set; }
        public List<string> Revealed { get; } = [];
        public bool IsDesktop => true;

        public Task<string?> ChooseSaveFilePathAsync(string defaultName) {
            LastDefaultName = defaultName;
            return Task.FromResult(SaveResult);
        }

        public Task OpenFileInFolderAsync(string path) {
            Revealed.Add(path);
            return Task.CompletedTask;
        }

        public Task OpenFileAsync(string path) => Task.CompletedTask;
        public Task OpenUrlAsync(string url) => Task.CompletedTask;
        public Task RestartAppAsync() => Task.CompletedTask;
        public Task<(int w, int h)> GetWindowSizeAsync() => Task.FromResult((0, 0));
        public Task<string?> ChooseFolderAsync() => Task.FromResult<string?>(null);
        public Task SetFolderHiddenAsync(string path, bool hidden) => Task.CompletedTask;
        public string DataRootDir => "";
    }

    private static IReadOnlyList<Mission> SampleMissions() => [];

    [Fact]
    public async Task DownloadCsvAsync_WritesDomainBytesToChosenPath_AndReveals() {
        var missions = SampleMissions();
        var path = Path.Combine(TempPath, "out.csv");
        var platform = new FakePlatform(path);
        var sink = new DesktopDownloadService(platform);

        await sink.DownloadCsvAsync(missions, "account.csv");

        Assert.True(File.Exists(path));
        Assert.Equal(MissionExport.MissionsToCsvBytes(missions), await File.ReadAllBytesAsync(path));
        Assert.Equal("account.csv", platform.LastDefaultName);
        Assert.Equal(path, Assert.Single(platform.Revealed));
    }

    [Fact]
    public async Task DownloadXlsxAsync_WritesDomainBytesToChosenPath_AndReveals() {
        var missions = SampleMissions();
        var path = Path.Combine(TempPath, "out.xlsx");
        var platform = new FakePlatform(path);
        var sink = new DesktopDownloadService(platform);

        await sink.DownloadXlsxAsync(missions, "account.xlsx");

        Assert.True(File.Exists(path));
        Assert.Equal(ZipEntries(MissionExport.MissionsToXlsxBytes(missions)), ZipEntries(await File.ReadAllBytesAsync(path)));
        Assert.Equal(path, Assert.Single(platform.Revealed));
    }

    private static List<(string Name, string Content)> ZipEntries(byte[] bytes) {
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        return [.. zip.Entries.Select(e => (e.FullName, ReadEntry(e)))];
    }

    private static string ReadEntry(ZipArchiveEntry entry) {
        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return Convert.ToHexString(buffer.ToArray());
    }

    [Fact]
    public async Task DownloadCsvAsync_Cancelled_WritesNothing_AndDoesNotReveal() {
        var platform = new FakePlatform(saveResult: null);
        var sink = new DesktopDownloadService(platform);

        await sink.DownloadCsvAsync(SampleMissions(), "account.csv");

        Assert.Empty(Directory.GetFiles(TempPath));
        Assert.Empty(platform.Revealed);
    }

    [Fact]
    public async Task DownloadXlsxAsync_Cancelled_WritesNothing_AndDoesNotReveal() {
        var platform = new FakePlatform(saveResult: null);
        var sink = new DesktopDownloadService(platform);

        await sink.DownloadXlsxAsync(SampleMissions(), "account.xlsx");

        Assert.Empty(Directory.GetFiles(TempPath));
        Assert.Empty(platform.Revealed);
    }
}
