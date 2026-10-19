using EggLedger.Domain.Reports;

namespace EggLedger.Web.Data;

public interface IDataOwner {
    string? CachedKey { get; }
    ValueTask<string> KeyAsync();
}

public sealed class LocalDataOwner : IDataOwner {
    public const string Key = "local";
    public string? CachedKey => Key;
    public ValueTask<string> KeyAsync() => ValueTask.FromResult(Key);
}

public sealed class LedgerDataStore(TimeProvider time, long maxRows = LedgerDataStore.DefaultMaxRows, TimeSpan? ttl = null) {
    public const long DefaultMaxRows = 6_000_000;

    public static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);

    private readonly TimeSpan _ttl = ttl ?? DefaultTtl;
    private readonly Lock _gate = new();
    private readonly Dictionary<(string Owner, string Account), Entry> _entries = [];
    private readonly List<(string Owner, string Account)> _order = [];

    public int Count {
        get {
            lock (_gate) return _entries.Count;
        }
    }

    public Task<LedgerReportData> GetAsync(string owner, string accountId, Func<Task<LedgerReportData>> load) =>
        Touch(owner, accountId, load).Data;

    public Task<T> GetDerivedAsync<T>(
        string owner,
        string accountId,
        string name,
        Func<Task<LedgerReportData>> load,
        Func<LedgerReportData, Task<T>> derive) where T : class {
        var entry = Touch(owner, accountId, load);
        lock (entry.Derived) {
            if (entry.Derived.TryGetValue(name, out var hit)) return (Task<T>)hit;
            var task = DeriveAsync(entry, name, derive);
            entry.Derived[name] = task;
            return task;
        }
    }

    public void Invalidate(string owner, string accountId) {
        lock (_gate) {
            Remove((owner, accountId));
        }
    }

    public void InvalidateAccount(string accountId) {
        lock (_gate) {
            foreach (var key in _order.Where(k => k.Account == accountId).ToList()) Remove(key);
        }
    }

    private async Task<T> DeriveAsync<T>(Entry entry, string name, Func<LedgerReportData, Task<T>> derive) {
        try {
            return await derive(await entry.Data.ConfigureAwait(false)).ConfigureAwait(false);
        } catch {
            lock (entry.Derived) {
                entry.Derived.Remove(name);
            }
            throw;
        }
    }

    private Entry Touch(string owner, string accountId, Func<Task<LedgerReportData>> load) {
        var key = (owner, accountId);
        lock (_gate) {
            if (_entries.TryGetValue(key, out var existing) && !Expired(existing)) {
                _order.Remove(key);
                _order.Add(key);
                return existing;
            }

            Remove(key);
            var entry = new Entry(time.GetUtcNow());
            _entries[key] = entry;
            _order.Add(key);
            entry.Data = LoadAsync(key, entry, load);
            return entry;
        }
    }

    private async Task<LedgerReportData> LoadAsync((string Owner, string Account) key, Entry entry, Func<Task<LedgerReportData>> load) {
        LedgerReportData data;
        try {
            data = await load().ConfigureAwait(false) ?? LedgerReportData.Empty;
        } catch {
            lock (_gate) {
                if (_entries.TryGetValue(key, out var current) && ReferenceEquals(current, entry)) Remove(key);
            }
            throw;
        }

        lock (_gate) {
            entry.Rows = data.Missions.Count + data.Drops.Count + data.Fuel.Count;
            Trim(key);
        }
        return data;
    }

    private void Trim((string Owner, string Account) keep) {
        long total = _entries.Values.Sum(e => e.Rows);
        int i = 0;
        while (total > maxRows && i < _order.Count) {
            var candidate = _order[i];
            if (candidate == keep) {
                i++;
                continue;
            }
            total -= _entries[candidate].Rows;
            Remove(candidate);
        }
    }

    private void Remove((string Owner, string Account) key) {
        _entries.Remove(key);
        _order.Remove(key);
    }

    private bool Expired(Entry entry) => time.GetUtcNow() - entry.LoadedAt >= _ttl;

    private sealed class Entry(DateTimeOffset loadedAt) {
        public DateTimeOffset LoadedAt { get; } = loadedAt;
        public Task<LedgerReportData> Data { get; set; } = null!;
        public long Rows { get; set; }
        public Dictionary<string, object> Derived { get; } = [with(StringComparer.Ordinal)];
    }
}
