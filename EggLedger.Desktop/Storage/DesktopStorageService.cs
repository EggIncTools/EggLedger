using EggLedger.Web.Platform;

namespace EggLedger.Desktop.Storage;

public enum StoragePart {
    Internal,
    Exports,
    Logs,
}

public sealed class DesktopStorageService(string rootDir, IPlatformCapabilities platform, Action<string> writeBootstrap) : IStorageManagement {
    private readonly string _rootDir = rootDir;
    private readonly IPlatformCapabilities _platform = platform;
    private readonly Action<string> _writeBootstrap = writeBootstrap;

    public DesktopStorageService(string rootDir, IPlatformCapabilities platform)
        : this(rootDir, platform, StoragePaths.WriteBootstrapConfig) {
    }

    public string GetDataRootDir() => StoragePaths.ResolveDataRootDir(_rootDir);

    public Task BackupAsync(string destPath, bool db, bool exports, bool logs) {
        return Task.Run(() => {
            if (db) {
                CopyPart(StoragePart.Internal, destPath);
            }
            if (exports) {
                CopyPart(StoragePart.Exports, destPath);
            }
            if (logs) {
                CopyPart(StoragePart.Logs, destPath);
            }
        });
    }

    public async Task MoveAsync(string destPath) {
        if (PathsEqual(destPath, GetDataRootDir())) {
            throw new InvalidOperationException("destination is the current data root");
        }
        await Task.Run(() => CopyAllAndWriteBootstrap(destPath));
        await _platform.RestartAppAsync();
    }

    private void CopyAllAndWriteBootstrap(string destPath) {
        CopyPart(StoragePart.Internal, destPath);
        CopyPart(StoragePart.Exports, destPath);
        CopyPart(StoragePart.Logs, destPath);
        _writeBootstrap(destPath);
    }

    private void CopyPart(StoragePart part, string destPath) {
        var source = SourceDir(part);
        if (!Directory.Exists(source)) {
            return;
        }
        StoragePaths.CopyDir(source, Path.Combine(destPath, SubDirName(part)));
    }

    private string SourceDir(StoragePart part) => part switch {
        StoragePart.Internal => StoragePaths.ResolveInternalDir(_rootDir),
        StoragePart.Exports => StoragePaths.ResolveExportsDir(_rootDir),
        StoragePart.Logs => StoragePaths.ResolveLogsDir(_rootDir),
        _ => throw new ArgumentOutOfRangeException(nameof(part)),
    };

    private static string SubDirName(StoragePart part) => part switch {
        StoragePart.Internal => "internal",
        StoragePart.Exports => "exports",
        StoragePart.Logs => "logs",
        _ => throw new ArgumentOutOfRangeException(nameof(part)),
    };

    private static bool PathsEqual(string a, string b) {
        var comparison = OperatingSystem.IsLinux()
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
        return string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            comparison);
    }
}
