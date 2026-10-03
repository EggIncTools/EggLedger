using EggLedger.Desktop.Storage;

namespace EggLedger.Desktop.Tests;

public sealed class StoragePathsTests {
    [Fact]
    public void Resolvers_FallBackToRootDir_WhenNoBootstrap() {

        var root = Path.Combine(Path.GetTempPath(), "egl_paths_" + Guid.NewGuid().ToString("N"));

        var dataRoot = StoragePaths.ResolveDataRootDir(root);
        var internalDir = StoragePaths.ResolveInternalDir(root);
        var exportsDir = StoragePaths.ResolveExportsDir(root);
        var logsDir = StoragePaths.ResolveLogsDir(root);

        Assert.Equal(Path.Combine(dataRoot, "internal"), internalDir);
        Assert.Equal(Path.Combine(dataRoot, "exports"), exportsDir);
        Assert.Equal(Path.Combine(dataRoot, "logs"), logsDir);
    }

    [Fact]
    public void CopyFile_CreatesDestinationDirectory() {
        using var baseDir = new TempDir("egl_copy");
        var src = baseDir.Write("src.txt", "hello");

        var dst = baseDir.File(Path.Combine("nested", "deep", "out.txt"));
        StoragePaths.CopyFile(src, dst);

        Assert.True(File.Exists(dst));
        Assert.Equal("hello", File.ReadAllText(dst));
    }

    [Fact]
    public void CopyDir_ReplicatesTree() {
        using var baseDir = new TempDir("egl_copydir");
        baseDir.Write(Path.Combine("src", "a.txt"), "A");
        baseDir.Write(Path.Combine("src", "sub", "b.txt"), "B");

        var dst = baseDir.File("dst");
        StoragePaths.CopyDir(baseDir.File("src"), dst);

        Assert.Equal("A", File.ReadAllText(Path.Combine(dst, "a.txt")));
        Assert.Equal("B", File.ReadAllText(Path.Combine(dst, "sub", "b.txt")));
    }
}
