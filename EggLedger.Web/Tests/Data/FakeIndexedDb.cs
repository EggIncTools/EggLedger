using EggLedger.Web.Data;

namespace EggLedger.Web.Tests.Data;

public sealed class FakeIndexedDb : IIndexedDb {


    private readonly Lock _gate = new();
    private readonly Dictionary<string, List<object>> _stores = [];
    private long _nextId;

    public void Seed(string store, object row) {
        lock (_gate) {
            if (row is ArtifactDropRow { Id: null } drop) {
                row = drop with { Id = ++_nextId };
            }
            if (row is FuelRow { Id: null } fuel) {
                row = fuel with { Id = ++_nextId };
            }
            if (!_stores.TryGetValue(store, out var list)) {
                list = [];
                _stores[store] = list;
            }
            list.Add(row);
        }
    }

    private static string? KeyOf(object row) => row switch {
        ReportRow r => r.Id,
        ReportGroupRow g => g.Id,
        PinnedReportRow p => p.Id,
        SettingRow s => s.Key,
        BackupRow b => b.PlayerId,
        InFlightMissionRow f => f.PlayerId + ":" + f.MissionId,
        ArtifactDropRow { Id: { } id } => id.ToString(),
        FuelRow { Id: { } id } => id.ToString(),
        _ => null,
    };

    public ValueTask<T[]> GetAllByIndexAsync<T>(string store, string index, object value) {
        lock (_gate) {
            if (!_stores.TryGetValue(store, out var list)) {
                return new ValueTask<T[]>([]);
            }

            var matches = list.OfType<T>()
                .Where(row => IndexValue(row, index)?.Equals(value) == true)
                .ToArray();
            return new ValueTask<T[]>(matches);
        }
    }

    public ValueTask<T[]> GetAllByIndexProjectedAsync<T>(string store, string index, object value) {
        if (typeof(T) == typeof(MissionMetaRow)) {
            lock (_gate) {
                var list = _stores.TryGetValue(store, out var l) ? l : [];
                var matches = list.OfType<MissionRow>()
                    .Where(r => r.PlayerId.Equals(value))
                    .Select(r => (T)(object)ToMetaRow(r))
                    .ToArray();
                return new ValueTask<T[]>(matches);
            }
        }
        return GetAllByIndexAsync<T>(store, index, value);
    }

    private static MissionMetaRow ToMetaRow(MissionRow r) => new() {
        PlayerId = r.PlayerId,
        MissionId = r.MissionId,
        StartTimestamp = r.StartTimestamp,
        MissionType = r.MissionType,
        Ship = r.Ship,
        DurationType = r.DurationType,
        Level = r.Level,
        Capacity = r.Capacity,
        IsDubCap = r.IsDubCap,
        IsBuggedCap = r.IsBuggedCap,
        Target = r.Target,
        ReturnTimestamp = r.ReturnTimestamp,
        NominalCapacity = r.NominalCapacity,
    };

    public ValueTask<T[]> GetAllAsync<T>(string store) {
        lock (_gate) {
            if (!_stores.TryGetValue(store, out var list)) {
                return new ValueTask<T[]>([]);
            }
            return new ValueTask<T[]>([.. list.OfType<T>()]);
        }
    }

    private static object? IndexValue<T>(T row, string index) => (row, index) switch {
        (MissionRow m, "player_id") => m.PlayerId,
        (InFlightMissionRow f, "player_id") => f.PlayerId,
        (ArtifactDropRow d, "player_id") => d.PlayerId,
        (FuelRow d, "player_id") => d.PlayerId,
        (ReportRow r, "account_id") => r.AccountId,
        (ReportGroupRow g, "account_id") => g.AccountId,
        (PinnedReportRow p, "account_id") => p.AccountId,
        _ => throw new NotSupportedException($"FakeIndexedDb has no index '{index}' for {typeof(T).Name}"),
    };

    public ValueTask PutAsync(string store, object value) {
        lock (_gate) {
            PutLocked(store, value);
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask<int> PutManyAsync(string store, IEnumerable<object> values) {
        int n = 0;
        lock (_gate) {
            foreach (var v in values) {

                PutLocked(store, v);
                n++;
            }
        }
        return new ValueTask<int>(n);
    }

    private void PutLocked(string store, object value) {
        if (!_stores.TryGetValue(store, out var list)) {
            list = [];
            _stores[store] = list;
        }

        if (value is ArtifactDropRow { Id: null } drop) {
            value = drop with { Id = ++_nextId };
        }
        if (value is FuelRow { Id: null } fuel) {
            value = fuel with { Id = ++_nextId };
        }

        var key = KeyOf(value);
        if (key is not null) {
            int i = list.FindIndex(r => KeyOf(r) == key);
            if (i >= 0) {
                list[i] = value;
                return;
            }
        }
        list.Add(value);
    }

    public ValueTask<T?> GetAsync<T>(string store, object key) {
        lock (_gate) {
            if (!_stores.TryGetValue(store, out var list)) {
                return new ValueTask<T?>(default(T));
            }
            var match = list.OfType<T>().FirstOrDefault(r => KeyMatches(r!, key));
            return new ValueTask<T?>(match);
        }
    }



    private static bool KeyMatches(object row, object key) => (row, key) switch {
        (MissionRow m, object[] { Length: 2 } k) => m.PlayerId.Equals(k[0]) && m.MissionId.Equals(k[1]),
        (InFlightMissionRow f, object[] { Length: 2 } k) => f.PlayerId.Equals(k[0]) && f.MissionId.Equals(k[1]),
        (ArtifactDropRow d, long id) => d.Id == id,
        (FuelRow d, long id) => d.Id == id,
        _ => KeyOf(row)?.Equals(key) == true,
    };

    public ValueTask DeleteAsync(string store, object key) {
        lock (_gate) {
            if (_stores.TryGetValue(store, out var list)) {
                list.RemoveAll(r => KeyMatches(r, key));
            }
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask ClearAsync(string store) {
        lock (_gate) {
            _stores.Remove(store);
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask<int> CountAsync(string store) {
        lock (_gate) {
            return new(_stores.TryGetValue(store, out var list) ? list.Count : 0);
        }
    }
}
