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
        var baseDir = Path.Combine(Path.GetTempPath(), "egl_copy_" + Guid.NewGuid().ToString("N"));
        try {
            var src = Path.Combine(baseDir, "src.txt");
            Directory.CreateDirectory(baseDir);
            File.WriteAllText(src, "hello");

            var dst = Path.Combine(baseDir, "nested", "deep", "out.txt");
            StoragePaths.CopyFile(src, dst);

            Assert.True(File.Exists(dst));
            Assert.Equal("hello", File.ReadAllText(dst));
        } finally {
            if (Directory.Exists(baseDir)) {
                Directory.Delete(baseDir, recursive: true);
            }
        }
    }

    [Fact]
    public void CopyDir_ReplicatesTree() {
        var baseDir = Path.Combine(Path.GetTempPath(), "egl_copydir_" + Guid.NewGuid().ToString("N"));
        try {
            var src = Path.Combine(baseDir, "src");
            Directory.CreateDirectory(Path.Combine(src, "sub"));
            File.WriteAllText(Path.Combine(src, "a.txt"), "A");
            File.WriteAllText(Path.Combine(src, "sub", "b.txt"), "B");

            var dst = Path.Combine(baseDir, "dst");
            StoragePaths.CopyDir(src, dst);

            Assert.Equal("A", File.ReadAllText(Path.Combine(dst, "a.txt")));
            Assert.Equal("B", File.ReadAllText(Path.Combine(dst, "sub", "b.txt")));
        } finally {
            if (Directory.Exists(baseDir)) {
                Directory.Delete(baseDir, recursive: true);
            }
        }
    }
}
