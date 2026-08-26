namespace EggLedger.Web.Data;

public sealed class IndexedDbSettings(IIndexedDb db) {
    private readonly IIndexedDb _db = db ?? throw new ArgumentNullException(nameof(db));
    private Dictionary<string, string>? _cache;

    public async Task<Dictionary<string, string>> GetAllSettingsAsync() {
        if (_cache is { } cached) {
            return new Dictionary<string, string>(cached);
        }

        var rows = await _db.GetAllAsync<SettingRow>(IndexedDbStores.Settings);
        var result = new Dictionary<string, string>(rows.Length);
        foreach (var row in rows) {
            result[row.Key] = row.Value;
        }
        _cache = result;
        return new Dictionary<string, string>(result);
    }

    public async Task SetSettingAsync(string key, string value) {
        await _db.PutAsync(IndexedDbStores.Settings, new SettingRow { Key = key, Value = value });
        if (_cache is { } cached) {
            cached[key] = value;
        }
    }

    public async Task RemoveSettingAsync(string key) {
        await _db.DeleteAsync(IndexedDbStores.Settings, key);
        _cache?.Remove(key);
    }

    public async Task SetSettingsAsync(IReadOnlyDictionary<string, string> settings) {
        var rows = settings.Select(kv => (object)new SettingRow { Key = kv.Key, Value = kv.Value });
        await _db.PutManyAsync(IndexedDbStores.Settings, rows);
        if (_cache is { } cached) {
            foreach (var kv in settings) {
                cached[kv.Key] = kv.Value;
            }
        }
    }
}
