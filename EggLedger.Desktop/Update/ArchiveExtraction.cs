using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace EggLedger.Desktop.Update;

public static class ArchiveExtraction {
    private const UnixFileMode ExecutableMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
        | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    public static bool IsArchive(string assetName) =>
        assetName.EndsWith(".tar.gz", StringComparison.Ordinal)
        || assetName.EndsWith(".zip", StringComparison.Ordinal);

    public static void Extract(string archivePath, string destPath) {
        if (archivePath.EndsWith(".tar.gz", StringComparison.Ordinal)) {
            ExtractFromTarGz(archivePath, destPath);
        } else if (archivePath.EndsWith(".zip", StringComparison.Ordinal)) {
            ExtractFromZip(archivePath, destPath);
        } else {
            throw new InvalidOperationException($"unsupported archive format: {Path.GetFileName(archivePath)}");
        }

        SetExecutableBit(destPath);
    }

    private static void ExtractFromTarGz(string archivePath, string destPath) {
        using var file = File.OpenRead(archivePath);
        using var gz = new GZipStream(file, CompressionMode.Decompress);
        using var tar = new TarReader(gz);

        TarEntry? entry;
        while ((entry = tar.GetNextEntry()) is not null) {
            if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile)) continue;
            if (entry.Length <= 0 || entry.DataStream is null) {
                continue;
            }
            using var dst = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None);
            entry.DataStream.CopyTo(dst);
            return;
        }

        throw new InvalidOperationException("no regular file found in archive");
    }

    private static void ExtractFromZip(string archivePath, string destPath) {
        using var zip = ZipFile.OpenRead(archivePath);

        ZipArchiveEntry? target = null;
        foreach (var entry in zip.Entries) {
            if (IsDirectoryEntry(entry)) continue;
            if (entry.FullName.Contains("/MacOS/", StringComparison.Ordinal)) {
                target = entry;
                break;
            }
        }

        if (target is null) {
            foreach (var entry in zip.Entries) {
                if (IsDirectoryEntry(entry)) continue;
                if (Path.GetExtension(entry.FullName).Length == 0) {
                    target = entry;
                    break;
                }
            }
        }

        if (target is null) throw new InvalidOperationException("no suitable binary found in zip archive");

        using var src = target.Open();
        using var dst = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None);
        src.CopyTo(dst);
    }

    private static bool IsDirectoryEntry(ZipArchiveEntry entry) =>
        entry.FullName.EndsWith('/') || string.IsNullOrEmpty(entry.Name);

    private static void SetExecutableBit(string path) {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        File.SetUnixFileMode(path, ExecutableMode);
    }
}
