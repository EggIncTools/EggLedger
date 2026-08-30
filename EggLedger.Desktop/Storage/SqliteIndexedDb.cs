using System.Globalization;
using System.Text.Json;
using EggLedger.Web.Data;
using Microsoft.Data.Sqlite;

namespace EggLedger.Desktop.Storage;

public sealed class SqliteIndexedDb : IIndexedDb {
    private readonly SqliteDatabase _missionDb;
    private readonly SqliteDatabase _reportDb;
    private readonly Dictionary<string, StoreMeta> _stores;
    private readonly Lock _gate;

    public SqliteIndexedDb(SqliteDatabase missionDb, SqliteDatabase reportDb) {
        _missionDb = missionDb ?? throw new ArgumentNullException(nameof(missionDb));
        _reportDb = reportDb ?? throw new ArgumentNullException(nameof(reportDb));
        _gate = _missionDb.Gate;
        _stores = BuildStoreMeta();
        BindBlobColumns();
    }

    private static readonly JsonSerializerOptions JsonOpts = Rows.JsonOptions;

    public ValueTask PutAsync(string store, object value) {
        lock (_gate) {
            var meta = Meta(store);
            Upsert(meta, value);
            return ValueTask.CompletedTask;
        }
    }

    public ValueTask<int> PutManyAsync(string store, IEnumerable<object> values) {
        lock (_gate) {
            var meta = Meta(store);
            var connection = Conn(meta);
            using var tx = connection.BeginTransaction();
            int count = 0;
            foreach (var value in values) {
                Upsert(meta, value, tx);
                count++;
            }
            tx.Commit();
            return ValueTask.FromResult(count);
        }
    }

    public ValueTask<T?> GetAsync<T>(string store, object key) {
        lock (_gate) {
            var meta = Meta(store);
            var connection = Conn(meta);
            using var cmd = connection.CreateCommand();
            var (where, keyArgs) = KeyPredicate(meta, key);
            cmd.CommandText = $"SELECT * FROM {meta.Table} WHERE {where} LIMIT 1;";
            BindArgs(cmd, keyArgs);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) {
                return ValueTask.FromResult<T?>(default);
            }
            return ValueTask.FromResult<T?>(Materialize<T>(meta, reader));
        }
    }

    public ValueTask<T[]> GetAllAsync<T>(string store) {
        lock (_gate) {
            var meta = Meta(store);
            var connection = Conn(meta);
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"SELECT * FROM {meta.Table};";
            return ValueTask.FromResult(ReadAll<T>(meta, cmd));
        }
    }

    public ValueTask<T[]> GetAllByIndexAsync<T>(string store, string index, object value) {
        lock (_gate) {
            index = IndexedDbStores.ValidIndex(index);
            var meta = Meta(store);
            var connection = Conn(meta);
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"SELECT * FROM {meta.Table} WHERE {index} = ?;";
            BindArgs(cmd, [value]);
            return ValueTask.FromResult(ReadAll<T>(meta, cmd));
        }
    }

    public ValueTask<T[]> GetAllByIndexProjectedAsync<T>(string store, string index, object value) {
        lock (_gate) {
            index = IndexedDbStores.ValidIndex(index);
            var meta = Meta(store);
            var connection = Conn(meta);
            var cols = string.Join(", ", RowColumns.Of<T>());
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"SELECT {cols} FROM {meta.Table} WHERE {index} = ?;";
            BindArgs(cmd, [value]);
            return ValueTask.FromResult(ReadAll<T>(meta, cmd));
        }
    }

    public ValueTask DeleteAsync(string store, object key) {
        lock (_gate) {
            var meta = Meta(store);
            var connection = Conn(meta);
            using var cmd = connection.CreateCommand();
            var (where, keyArgs) = KeyPredicate(meta, key);
            cmd.CommandText = $"DELETE FROM {meta.Table} WHERE {where};";
            BindArgs(cmd, keyArgs);
            cmd.ExecuteNonQuery();
            return ValueTask.CompletedTask;
        }
    }

    public ValueTask ClearAsync(string store) {
        lock (_gate) {
            var meta = Meta(store);
            var connection = Conn(meta);
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"DELETE FROM {meta.Table};";
            cmd.ExecuteNonQuery();
            return ValueTask.CompletedTask;
        }
    }

    public ValueTask<int> CountAsync(string store) {
        lock (_gate) {
            var meta = Meta(store);
            var connection = Conn(meta);
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM {meta.Table};";
            var result = cmd.ExecuteScalar();
            return ValueTask.FromResult(result is null ? 0 : Convert.ToInt32(result, CultureInfo.InvariantCulture));
        }
    }

    private void Upsert(StoreMeta meta, object value, SqliteTransaction? tx = null) {


        using var doc = JsonSerializer.SerializeToDocument(value, value.GetType(), JsonOpts);
        var props = new List<(string Col, JsonElement Val)>();
        foreach (var prop in doc.RootElement.EnumerateObject()) {
            if (meta.AutoIncrementColumn == prop.Name && prop.Value.ValueKind is JsonValueKind.Null) {
                continue;
            }
            props.Add((prop.Name, prop.Value));
        }

        var connection = Conn(meta);
        using var cmd = connection.CreateCommand();
        if (tx is not null) {
            cmd.Transaction = tx;
        }

        var cols = props.Select(p => p.Col).ToList();
        var placeholders = props.Select((_, i) => "@p" + i.ToString(CultureInfo.InvariantCulture)).ToList();

        if (meta.UpsertByDelete) {


            DeleteByKey(meta, props, connection, tx);
        }

        string conflict;
        if (meta.AutoIncrementColumn is not null || meta.UpsertByDelete) {


            conflict = "";
        } else {
            var updates = cols
                .Where(c => !meta.KeyColumns.Contains(c))
                .Select(c => $"{c} = excluded.{c}");
            conflict = $" ON CONFLICT({string.Join(", ", meta.KeyColumns)}) DO UPDATE SET {string.Join(", ", updates)}";
        }

        cmd.CommandText =
            $"INSERT INTO {meta.Table} ({string.Join(", ", cols)}) VALUES ({string.Join(", ", placeholders)}){conflict};";
        for (var i = 0; i < props.Count; i++) {
            cmd.Parameters.AddWithValue(placeholders[i], JsonRowCodec.JsonToDbValue(props[i].Val, meta.BlobColumns.Contains(props[i].Col), JsonRowCodec.Sqlite));
        }
        cmd.ExecuteNonQuery();
    }



    private static void DeleteByKey(
        StoreMeta meta, List<(string Col, JsonElement Val)> props, SqliteConnection connection, SqliteTransaction? tx) {
        using var del = connection.CreateCommand();
        if (tx is not null) {
            del.Transaction = tx;
        }
        var clauses = new List<string>(meta.KeyColumns.Length);
        var k = 0;
        foreach (var keyCol in meta.KeyColumns) {
            var match = props.First(p => p.Col == keyCol);
            var name = "@d" + k.ToString(CultureInfo.InvariantCulture);
            clauses.Add($"{keyCol} = {name}");
            del.Parameters.AddWithValue(name, JsonRowCodec.JsonToDbValue(match.Val, meta.BlobColumns.Contains(keyCol), JsonRowCodec.Sqlite));
            k++;
        }
        del.CommandText = $"DELETE FROM {meta.Table} WHERE {string.Join(" AND ", clauses)};";
        del.ExecuteNonQuery();
    }

    private static T[] ReadAll<T>(StoreMeta meta, SqliteCommand cmd) {
        var result = new List<T>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) {
            result.Add(Materialize<T>(meta, reader));
        }
        return [.. result];
    }

    private static T Materialize<T>(StoreMeta meta, SqliteDataReader reader) =>
        JsonRowCodec.Materialize<T>(
            reader, meta.Table,
            isBool: meta.BoolColumns.Contains,
            isBlob: meta.BlobColumns.Contains,
            JsonOpts);

    private SqliteConnection Conn(StoreMeta meta) =>
        meta.UseReportDb ? _reportDb.Connection : _missionDb.Connection;

    private StoreMeta Meta(string store) =>
        _stores.TryGetValue(store, out var meta)
            ? meta
            : throw new ArgumentException($"unknown store {store}", nameof(store));


    private static (string where, object[] args) KeyPredicate(StoreMeta meta, object key) =>
        JsonRowCodec.KeyPredicate(meta.Table, meta.KeyColumns, key, c => c, JsonRowCodec.Sqlite);

    private static void BindArgs(SqliteCommand cmd, object[] args) =>
        cmd.CommandText = SqlPlaceholderBinder.Rewrite(cmd.CommandText, args, cmd, "k");

    private static Dictionary<string, StoreMeta> BuildStoreMeta() => new(StringComparer.Ordinal) {
        [IndexedDbStores.Mission] = new StoreMeta(
            "mission", useReportDb: false,
            keyColumns: ["player_id", "mission_id"],
            autoIncrementColumn: null,
            boolColumns: ["is_dub_cap", "is_bugged_cap"]),
        [IndexedDbStores.InFlightMission] = new StoreMeta(
            "inflight_mission", useReportDb: false,
            keyColumns: ["player_id", "mission_id"],
            autoIncrementColumn: null,
            boolColumns: []),
        [IndexedDbStores.Backup] = new StoreMeta(
            "backup", useReportDb: false,
            keyColumns: ["player_id"],
            autoIncrementColumn: null,
            boolColumns: [],

            upsertByDelete: true),
        [IndexedDbStores.ArtifactDrops] = new StoreMeta(
            "artifact_drops", useReportDb: false,
            keyColumns: ["id"],
            autoIncrementColumn: "id",
            boolColumns: []),
        [IndexedDbStores.MissionFuel] = new StoreMeta(
            "mission_fuel", useReportDb: false,
            keyColumns: ["id"],
            autoIncrementColumn: "id",
            boolColumns: []),
        [IndexedDbStores.Settings] = new StoreMeta(
            "settings", useReportDb: false,
            keyColumns: ["key"],
            autoIncrementColumn: null,
            boolColumns: []),
        [IndexedDbStores.Reports] = new StoreMeta(
            "reports", useReportDb: true,
            keyColumns: ["id"],
            autoIncrementColumn: null,
            boolColumns: ["menno_enabled"]),
        [IndexedDbStores.ReportGroups] = new StoreMeta(
            "report_groups", useReportDb: true,
            keyColumns: ["id"],
            autoIncrementColumn: null,
            boolColumns: []),
        [IndexedDbStores.PinnedReports] = new StoreMeta(
            "pinned_reports", useReportDb: true,
            keyColumns: ["id"],
            autoIncrementColumn: null,
            boolColumns: []),
    };



    private void BindBlobColumns() {
        foreach (var meta in _stores.Values) {
            var connection = Conn(meta);
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info({meta.Table});";
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) {

                var name = reader.GetString(1);
                var declaredType = reader.IsDBNull(2) ? "" : reader.GetString(2);
                if (declaredType.Equals("BLOB", StringComparison.OrdinalIgnoreCase)) {
                    meta.BlobColumns.Add(name);
                }
            }
        }
    }

    private sealed class StoreMeta {
        public StoreMeta(
            string table, bool useReportDb, string[] keyColumns, string? autoIncrementColumn,
            string[] boolColumns, bool upsertByDelete = false) {
            Table = table;
            UseReportDb = useReportDb;
            KeyColumns = keyColumns;
            AutoIncrementColumn = autoIncrementColumn;
            BoolColumns = new HashSet<string>(boolColumns, StringComparer.Ordinal);
            BlobColumns = new HashSet<string>(StringComparer.Ordinal);
            UpsertByDelete = upsertByDelete;
        }

        public string Table { get; }
        public bool UseReportDb { get; }
        public string[] KeyColumns { get; }
        public string? AutoIncrementColumn { get; }
        public bool UpsertByDelete { get; }
        public HashSet<string> BoolColumns { get; }
        public HashSet<string> BlobColumns { get; }
    }
}
