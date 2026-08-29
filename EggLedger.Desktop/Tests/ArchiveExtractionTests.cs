using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using EggLedger.Desktop.Update;

namespace EggLedger.Desktop.Tests;

public sealed class ArchiveExtractionTests {
    private static readonly byte[] Payload = Encoding.ASCII.GetBytes("#!/bin/sh\nfake-egg-binary\n");

    private sealed class TempDir : IDisposable {
        public string Path { get; } =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "egg-arc-" + Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(Path);

        public string File(string name) => System.IO.Path.Combine(Path, name);

        public void Dispose() {
            try {
                Directory.Delete(Path, recursive: true);
            } catch (IOException) {
            }
        }
    }

    private static void WriteTarGz(string path, params (string Name, byte[] Data)[] entries) {
        using var file = File.Create(path);
        using var gz = new GZipStream(file, CompressionLevel.Fastest);
        using var tar = new TarWriter(gz, TarEntryFormat.Pax);
        foreach (var (name, data) in entries) {
            var entry = new PaxTarEntry(TarEntryType.RegularFile, name) {
                DataStream = new MemoryStream(data),
            };
            tar.WriteEntry(entry);
        }
    }

    private static void WriteZip(string path, params (string Name, byte[] Data)[] entries) {
        using var file = File.Create(path);
        using var zip = new ZipArchive(file, ZipArchiveMode.Create);
        foreach (var (name, data) in entries) {
            var e = zip.CreateEntry(name);
            using var s = e.Open();
            s.Write(data);
        }
    }

    private static void AssertExecutableOnUnix(string path) {
        if (OperatingSystem.IsWindows()) {
            return;
        }
        var mode = File.GetUnixFileMode(path);
        Assert.True(mode.HasFlag(UnixFileMode.UserExecute), "extracted binary should have the user-execute bit");
    }

    [Fact]
    public void Extract_TarGz_PicksFirstRegularFile() {
        using var dir = new TempDir();
        var archive = dir.File("EggLedger-linux.tar.gz");
        var dest = dir.File("EggLedger_new");
        WriteTarGz(archive, ("EggLedger", Payload), ("README.txt", Encoding.ASCII.GetBytes("ignored")));

        ArchiveExtraction.Extract(archive, dest);

        Assert.Equal(Payload, File.ReadAllBytes(dest));
        AssertExecutableOnUnix(dest);
    }

    [Fact]
    public void Extract_TarGz_SkipsEmptyEntries() {
        using var dir = new TempDir();
        var archive = dir.File("EggLedger-linux.tar.gz");
        var dest = dir.File("EggLedger_new");
        WriteTarGz(archive, ("placeholder", []), ("EggLedger", Payload));

        ArchiveExtraction.Extract(archive, dest);

        Assert.Equal(Payload, File.ReadAllBytes(dest));
    }

    [Fact]
    public void Extract_TarGz_NoRegularFile_Throws() {
        using var dir = new TempDir();
        var archive = dir.File("EggLedger-linux.tar.gz");
        var dest = dir.File("EggLedger_new");
        WriteTarGz(archive, ("empty", []));

        Assert.Throws<InvalidOperationException>(() => ArchiveExtraction.Extract(archive, dest));
    }

    [Fact]
    public void Extract_Zip_PrefersMacOsBundleEntry() {
        using var dir = new TempDir();
        var archive = dir.File("EggLedger-mac.zip");
        var dest = dir.File("EggLedger_new");
        WriteZip(
            archive,
            ("EggLedger.app/Contents/Info.plist", Encoding.ASCII.GetBytes("plist")),
            ("EggLedger.app/Contents/MacOS/EggLedger", Payload));

        ArchiveExtraction.Extract(archive, dest);

        Assert.Equal(Payload, File.ReadAllBytes(dest));
        AssertExecutableOnUnix(dest);
    }

    [Fact]
    public void Extract_Zip_FallsBackToFirstExtensionlessFile() {
        using var dir = new TempDir();
        var archive = dir.File("EggLedger-mac.zip");
        var dest = dir.File("EggLedger_new");
        WriteZip(
            archive,
            ("notes.txt", Encoding.ASCII.GetBytes("ignored")),
            ("EggLedger", Payload));

        ArchiveExtraction.Extract(archive, dest);

        Assert.Equal(Payload, File.ReadAllBytes(dest));
    }

    [Fact]
    public void Extract_Zip_NoSuitableEntry_Throws() {
        using var dir = new TempDir();
        var archive = dir.File("EggLedger-mac.zip");
        var dest = dir.File("EggLedger_new");
        WriteZip(archive, ("readme.md", Encoding.ASCII.GetBytes("x")));

        Assert.Throws<InvalidOperationException>(() => ArchiveExtraction.Extract(archive, dest));
    }

    [Fact]
    public void Extract_UnsupportedSuffix_Throws() {
        using var dir = new TempDir();
        var archive = dir.File("EggLedger.rar");
        File.WriteAllBytes(archive, [0, 1, 2]);

        Assert.Throws<InvalidOperationException>(() => ArchiveExtraction.Extract(archive, dir.File("out")));
    }

    [Theory]
    [InlineData("EggLedger-linux.tar.gz", true)]
    [InlineData("EggLedger-mac.zip", true)]
    [InlineData("EggLedger-mac-arm64.zip", true)]
    [InlineData("EggLedger.exe", false)]
    [InlineData("EggLedger", false)]
    public void IsArchive_MatchesArchiveSuffixes(string name, bool expected)
        => Assert.Equal(expected, ArchiveExtraction.IsArchive(name));
}
