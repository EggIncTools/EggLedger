using System.Globalization;
using System.Text.Json;
using EggLedger.Web.Data;
using Npgsql;
using NpgsqlTypes;

namespace EggLedger.Web.Server.Storage;

public sealed class PostgresIndexedDb : IIndexedDb {
    private readonly NpgsqlDataSource _source;
    private readonly CurrentUser _user;
    private readonly Dictionary<string, StoreMeta> _stores;

    public PostgresIndexedDb(NpgsqlDataSource source, CurrentUser user) {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _user = user ?? throw new ArgumentNullException(nameof(user));
        _stores = BuildStoreMeta();
    }




    private Task<Guid> UserAsync() => _user.RequireUserIdAsync();
    private Task<Guid?> TryUserAsync() => _user.GetUserIdAsync();
    private static readonly JsonSerializerOptions JsonOpts = Rows.JsonOptions;

    public async ValueTask PutAsync(string store, object value) {
        await using var conn = await _source.OpenConnectionAsync().ConfigureAwait(false);
        await UpsertAsync(Meta(store), value, conn, tx: null).ConfigureAwait(false);
    }

    public async ValueTask<int> PutManyAsync(string store, IEnumerable<object> values) {
        var meta = Meta(store);
        await using var conn = await _source.OpenConnectionAsync().ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync().ConfigureAwait(false);
        int count = 0;
        foreach (var value in values) {
            await UpsertAsync(meta, value, conn, tx).ConfigureAwait(false);
            count++;
        }
        await tx.CommitAsync().ConfigureAwait(false);
        return count;
    }

    public async ValueTask<T?> GetAsync<T>(string store, object key) {
        if (await TryUserAsync().ConfigureAwait(false) is not { } user) {
            return default;
        }
        var meta = Meta(store);
        var (where, keyArgs) = KeyPredicate(meta, key);
        await using var conn = await _source.OpenConnectionAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT * FROM {meta.Table} WHERE user_id = @user AND {where} LIMIT 1;";
        cmd.Parameters.AddWithValue("user", user);
        BindKeyArgs(cmd, keyArgs);
        await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        if (!await reader.ReadAsync().ConfigureAwait(false)) {
            return default;
        }
        return Materialize<T>(meta, reader);
    }

    public async ValueTask<T[]> GetAllAsync<T>(string store) {
        if (await TryUserAsync().ConfigureAwait(false) is not { } user) {
            return [];
        }
        var meta = Meta(store);
        await using var conn = await _source.OpenConnectionAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT * FROM {meta.Table} WHERE user_id = @user;";
        cmd.Parameters.AddWithValue("user", user);
        return await ReadAllAsync<T>(meta, cmd).ConfigureAwait(false);
    }

    public async ValueTask<T[]> GetAllByIndexAsync<T>(string store, string index, object value) {
        index = IndexedDbStores.ValidIndex(index);
        if (await TryUserAsync().ConfigureAwait(false) is not { } user) {
            return [];
        }
        var meta = Meta(store);
        await using var conn = await _source.OpenConnectionAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT * FROM {meta.Table} WHERE user_id = @user AND {Ident(index)} = @idx;";
        cmd.Parameters.AddWithValue("user", user);
        cmd.Parameters.AddWithValue("idx", value ?? (object)DBNull.Value);
        return await ReadAllAsync<T>(meta, cmd).ConfigureAwait(false);
    }

    public async ValueTask<T[]> GetAllByIndexProjectedAsync<T>(string store, string index, object value) {
        index = IndexedDbStores.ValidIndex(index);
        if (await TryUserAsync().ConfigureAwait(false) is not { } user) {
            return [];
        }
        var meta = Meta(store);
        var cols = string.Join(", ", RowColumns.Of<T>().Select(Ident));
        await using var conn = await _source.OpenConnectionAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {cols} FROM {meta.Table} WHERE user_id = @user AND {Ident(index)} = @idx;";
        cmd.Parameters.AddWithValue("user", user);
        cmd.Parameters.AddWithValue("idx", value ?? (object)DBNull.Value);
        return await ReadAllAsync<T>(meta, cmd).ConfigureAwait(false);
    }

    public async ValueTask DeleteAsync(string store, object key) {
        var meta = Meta(store);
        var (where, keyArgs) = KeyPredicate(meta, key);
        await using var conn = await _source.OpenConnectionAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DELETE FROM {meta.Table} WHERE user_id = @user AND {where};";
        cmd.Parameters.AddWithValue("user", await UserAsync());
        BindKeyArgs(cmd, keyArgs);
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async ValueTask ClearAsync(string store) {
        var meta = Meta(store);
        await using var conn = await _source.OpenConnectionAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DELETE FROM {meta.Table} WHERE user_id = @user;";
        cmd.Parameters.AddWithValue("user", await UserAsync());
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async ValueTask<int> CountAsync(string store) {
        if (await TryUserAsync().ConfigureAwait(false) is not { } user) {
            return 0;
        }
        var meta = Meta(store);
        await using var conn = await _source.OpenConnectionAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {meta.Table} WHERE user_id = @user;";
        cmd.Parameters.AddWithValue("user", user);
        var result = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
        return result is null or DBNull ? 0 : Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private async Task UpsertAsync(StoreMeta meta, object value, NpgsqlConnection conn, NpgsqlTransaction? tx) {
        using var doc = JsonSerializer.SerializeToDocument(value, value.GetType(), JsonOpts);
        var user = await UserAsync().ConfigureAwait(false);



        var cols = new List<string> { "user_id" };
        var values = new List<(string Param, object Value)> { ("p_user", user) };
        int p = 0;
        foreach (var prop in doc.RootElement.EnumerateObject()) {
            if (meta.AutoIncrementColumn == prop.Name && prop.Value.ValueKind is JsonValueKind.Null) {
                continue;
            }
            var param = "p" + p.ToString(CultureInfo.InvariantCulture);
            cols.Add(prop.Name);
            values.Add((param, JsonRowCodec.JsonToDbValue(prop.Value, meta.BlobColumns.Contains(prop.Name), JsonRowCodec.Postgres)));
            p++;
        }

        string conflict;
        if (meta.AutoIncrementColumn is not null) {
            conflict = "";
        } else {


            var keyCols = new List<string> { "user_id" };
            keyCols.AddRange(meta.KeyColumns);
            var updates = cols
                .Where(c => !keyCols.Contains(c, StringComparer.Ordinal))
                .Select(c => $"{Ident(c)} = excluded.{Ident(c)}");
            conflict = $" ON CONFLICT ({string.Join(", ", keyCols.Select(Ident))}) DO UPDATE SET {string.Join(", ", updates)}";
        }

        await using var cmd = conn.CreateCommand();
        if (tx is not null) {
            cmd.Transaction = tx;
        }
        var colSql = string.Join(", ", cols.Select(Ident));
        var valSql = string.Join(", ", values.Select(v => "@" + v.Param));
        cmd.CommandText = $"INSERT INTO {meta.Table} ({colSql}) VALUES ({valSql}){conflict};";
        foreach (var (param, val) in values) {
            if (val is byte[] bytes) {
                cmd.Parameters.Add(new NpgsqlParameter(param, NpgsqlDbType.Bytea) { Value = bytes });
            } else {
                cmd.Parameters.AddWithValue(param, val);
            }
        }
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static async Task<T[]> ReadAllAsync<T>(StoreMeta meta, NpgsqlCommand cmd) {
        var result = new List<T>();
        await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false)) {
            result.Add(Materialize<T>(meta, reader));
        }
        return [.. result];
    }

    private static T Materialize<T>(StoreMeta meta, NpgsqlDataReader reader) =>
        JsonRowCodec.Materialize<T>(
            reader, meta.Table,
            isBool: _ => false,
            isBlob: meta.BlobColumns.Contains,
            JsonOpts,
            skipColumn: name => name == "user_id");

    private static void BindKeyArgs(NpgsqlCommand cmd, object[] args) {
        for (var i = 0; i < args.Length; i++) {
            cmd.Parameters.AddWithValue("k" + i.ToString(CultureInfo.InvariantCulture), args[i] ?? DBNull.Value);
        }
    }


    private static (string where, object[] args) KeyPredicate(StoreMeta meta, object key) =>
        JsonRowCodec.KeyPredicate(meta.Table, meta.KeyColumns, key, Ident, JsonRowCodec.Postgres);



    private static string Ident(string name) {
        if (name.Contains('"', StringComparison.Ordinal)) {
            throw new ArgumentException($"illegal identifier {name}", nameof(name));
        }
        return "\"" + name + "\"";
    }

    private StoreMeta Meta(string store) =>
        _stores.TryGetValue(store, out var meta)
            ? meta
            : throw new ArgumentException($"unknown store {store}", nameof(store));

    private static Dictionary<string, StoreMeta> BuildStoreMeta() => new(StringComparer.Ordinal) {
        [IndexedDbStores.Mission] = new StoreMeta("el_mission", ["player_id", "mission_id"], autoIncrementColumn: null,
            blobColumns: ["complete_payload"]),
        [IndexedDbStores.InFlightMission] = new StoreMeta("el_inflight_mission", ["player_id", "mission_id"],
            autoIncrementColumn: null, blobColumns: []),
        [IndexedDbStores.Backup] = new StoreMeta("el_backup", ["player_id"], autoIncrementColumn: null,
            blobColumns: ["payload"]),
        [IndexedDbStores.ArtifactDrops] = new StoreMeta("el_artifact_drops", ["id"], autoIncrementColumn: "id",
            blobColumns: []),
        [IndexedDbStores.MissionFuel] = new StoreMeta("el_mission_fuel", ["id"], autoIncrementColumn: "id",
            blobColumns: []),
        [IndexedDbStores.Settings] = new StoreMeta("el_settings", ["key"], autoIncrementColumn: null,
            blobColumns: []),
        [IndexedDbStores.Reports] = new StoreMeta("el_reports", ["id"], autoIncrementColumn: null,
            blobColumns: []),
        [IndexedDbStores.ReportGroups] = new StoreMeta("el_report_groups", ["id"], autoIncrementColumn: null,
            blobColumns: []),
        [IndexedDbStores.PinnedReports] = new StoreMeta("el_pinned_reports", ["id"], autoIncrementColumn: null,
            blobColumns: []),
    };

    private sealed class StoreMeta {
        public StoreMeta(string table, string[] keyColumns, string? autoIncrementColumn, string[] blobColumns) {
            Table = table;
            KeyColumns = keyColumns;
            AutoIncrementColumn = autoIncrementColumn;
            BlobColumns = new HashSet<string>(blobColumns, StringComparer.Ordinal);
        }

        public string Table { get; }
        public string[] KeyColumns { get; }
        public string? AutoIncrementColumn { get; }
        public HashSet<string> BlobColumns { get; }
    }
}
