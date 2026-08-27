using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using EggLedger.Web.Ships;

namespace EggLedger.Web.Server.Ships;



public sealed class ShipAssetService {
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string _dir;
    private readonly ConcurrentDictionary<string, byte[]> _cache = new();

    public ShipManifest Manifest { get; }

    public ShipAssetService(string shipsDir) {
        _dir = shipsDir;
        var path = Path.Combine(_dir, "manifest.json");
        Manifest = File.Exists(path)
            ? JsonSerializer.Deserialize<ShipManifest>(File.ReadAllText(path), Json) ?? Empty()
            : Empty();
    }

    private static ShipManifest Empty() =>
        new("1", null, new Dictionary<string, ShipManifestEntry>());

    public bool TryGetPath(string key, out string fullPath) {
        fullPath = "";
        if (!ShipAssetKey.IsKnown(key)) return false;
        if (!Manifest.Ships.TryGetValue(key, out var entry)) return false;
        var candidate = Path.Combine(_dir, entry.File);
        if (!File.Exists(candidate)) return false;
        fullPath = candidate;
        return true;
    }

    public async Task<byte[]?> ReadAsync(string key, CancellationToken ct) {
        if (_cache.TryGetValue(key, out var cached)) return cached;
        if (!TryGetPath(key, out var path)) return null;
        var bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (sha != Manifest.Ships[key].Sha256) return null;
        _cache[key] = bytes;
        return bytes;
    }
}
