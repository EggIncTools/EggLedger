using EggLedger.Web.Services;

namespace EggLedger.Desktop.Storage;

public sealed class FileGameEventStore : IGameEventStore {
    private readonly string _path;

    public FileGameEventStore(string internalDir) {
        _path = Path.Combine(internalDir, "events.json");
    }

    public async Task<byte[]?> LoadAsync(CancellationToken cancellationToken = default) {
        if (!File.Exists(_path)) {
            return null;
        }
        return await File.ReadAllBytesAsync(_path, cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveAsync(byte[] utf8Json, CancellationToken cancellationToken = default) {
        var tmp = _path + ".tmp";
        await File.WriteAllBytesAsync(tmp, utf8Json, cancellationToken).ConfigureAwait(false);
        File.Move(tmp, _path, overwrite: true);
    }
}
