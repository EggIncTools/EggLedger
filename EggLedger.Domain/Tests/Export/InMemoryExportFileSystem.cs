using EggLedger.Domain.Export;

namespace EggLedger.Domain.Tests.Export;

internal sealed class InMemoryExportFileSystem : IExportFileSystem {
    private readonly Dictionary<string, long> _files = new(StringComparer.Ordinal);

    public void AddFile(string dir, string name, long size) {
        _files[Path.Combine(dir, name)] = size;
    }

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
