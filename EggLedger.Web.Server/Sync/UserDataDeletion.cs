using Npgsql;

namespace EggLedger.Web.Server.Sync;

public static class UserDataDeletion {
    public static readonly string[] StoredDataTables = [
        "el_mission",
        "el_inflight_mission",
        "el_backup",
        "el_artifact_drops",
        "el_settings",
        "el_reports",
        "el_report_groups",
        "el_mission_fuel",
        "el_pinned_reports"
    ];

    public static async Task<bool> DeleteUserAsync(
        NpgsqlDataSource source,
        Guid userId,
        CancellationToken ct = default) {
        await using var conn = await source.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);

        foreach (var table in await LiveTablesAsync(conn, tx, ct).ConfigureAwait(false)) {
            await using var del = conn.CreateCommand();
            del.Transaction = tx;
            del.CommandText = $"DELETE FROM {Ident(table)} WHERE user_id = @user";
            del.Parameters.AddWithValue("user", userId);
            await del.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await using var users = conn.CreateCommand();
        users.Transaction = tx;
        users.CommandText = "DELETE FROM users WHERE user_id = @user";
        users.Parameters.AddWithValue("user", userId);
        var rows = await users.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        await tx.CommitAsync(ct).ConfigureAwait(false);
        return rows > 0;
    }

    private static async Task<List<string>> LiveTablesAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        CancellationToken ct) {
        var live = new HashSet<string>(StringComparer.Ordinal);
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "SELECT t FROM unnest(@names::text[]) AS t WHERE to_regclass(quote_ident(t)) IS NOT NULL";
        cmd.Parameters.AddWithValue("names", StoredDataTables);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false)) {
            live.Add(reader.GetString(0));
        }

        return [.. StoredDataTables.Where(live.Contains)];
    }

    private static string Ident(string name) {
        if (!StoredDataTables.Contains(name, StringComparer.Ordinal)) {
            throw new ArgumentException($"unknown table {name}", nameof(name));
        }

        return "\"" + name + "\"";
    }
}
