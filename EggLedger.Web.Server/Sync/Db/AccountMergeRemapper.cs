using System.Security.Cryptography;
using EggIdentity.Client;
using EggIdentity.Contract;
using EggLedger.Domain.Crypto;
using Microsoft.AspNetCore.DataProtection;
using Npgsql;

namespace EggLedger.Web.Server.Sync.Db;

public sealed class AccountMergeRemapper(
    NpgsqlDataSource source,
    IdentityApiClient identity,
    IDataProtectionProvider dataProtection,
    ILogger<AccountMergeRemapper> logger) {
    internal sealed record RemapTable(string Name, string[] UniqueKey);

    internal static readonly RemapTable[] Tables = [
        new("el_mission", ["player_id", "mission_id"]),
        new("el_inflight_mission", ["player_id", "mission_id"]),
        new("el_backup", ["player_id"]),
        new("el_artifact_drops", ["mission_id", "player_id", "drop_index"]),
        new("el_mission_fuel", ["mission_id", "player_id", "fuel_index"]),
        new("el_settings", ["key"]),
        new("el_reports", ["id"]),
        new("el_report_groups", ["id"]),
        new("el_pinned_reports", ["id"]),
        new("blobs", ["name"]),
        new("sessions", []),
    ];

    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan Overlap = TimeSpan.FromSeconds(1);
    private const int FeedPageSize = 500;

    private readonly IDataProtector _keyProtector = dataProtection.CreateProtector("EggLedger.EncryptionKey");

    public async Task RunAsync(CancellationToken ct) {
        using var timer = new PeriodicTimer(Interval);
        try {
            do {
                await TrySweepAsync(ct);
            } while (await timer.WaitForNextTickAsync(ct));
        } catch (OperationCanceledException ex) {
            logger.LogDebug(ex, "merges: remapper stopped on shutdown");
        }
    }

    private async Task TrySweepAsync(CancellationToken ct) {
        try {
            var applied = await SweepAsync(ct);
            if (applied > 0)
                logger.LogInformation("merges: remapped {Count} account merge(s)", applied);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            logger.LogWarning(ex, "merges: sweep failed, retrying next interval");
        }
    }

    public async Task<int> SweepAsync(CancellationToken ct) {
        var applied = 0;
        var watermark = await ReadWatermarkAsync(ct);
        while (true) {
            var page = await identity.ListMergesAsync(watermark - Overlap, ct);
            foreach (var merge in page) {
                await RemapAsync(merge, ct);
                applied++;
            }
            if (page.Count < FeedPageSize)
                return applied;

            var last = page[^1].MergedAt;
            if (watermark is { } w && last <= w) {
                logger.LogWarning("merges: {Count} merges share merged_at {At}, feed page cannot advance", page.Count, last);
                return applied;
            }
            watermark = last;
        }
    }

    internal async Task RemapAsync(UserMergeResponse merge, CancellationToken ct) {
        var (merged, kept) = (merge.MergedUserId, merge.KeptUserId);
        await using var conn = await source.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        if (merged != kept)
            await MoveRowsAsync(conn, tx, merged, kept, ct);

        await ExecAsync(conn, tx,
            "INSERT INTO identity_merge_watermark (id, merged_at) VALUES (TRUE, $1) " +
            "ON CONFLICT (id) DO UPDATE SET merged_at = GREATEST(identity_merge_watermark.merged_at, EXCLUDED.merged_at)",
            ct, merge.MergedAt.ToUniversalTime());

        await tx.CommitAsync(ct);
    }

    private async Task MoveRowsAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Guid merged, Guid kept, CancellationToken ct) {
        var keys = await LockUsersAsync(conn, tx, merged, kept, ct);
        var hasMerged = keys.TryGetValue(merged, out var mergedKey);
        var hasKept = keys.TryGetValue(kept, out var keptKey);
        if (!hasMerged && !hasKept)
            return;

        foreach (var table in Tables.Where(t => t.UniqueKey.Length > 0))
            await ExecAsync(conn, tx, CollisionSql(table), ct, merged, kept);

        if (!hasKept) {
            await ExecAsync(conn, tx, "UPDATE users SET user_id = $2 WHERE user_id = $1", ct, merged, kept);
            return;
        }

        if (hasMerged)
            await RekeyAsync(conn, tx, merged, kept, mergedKey!, keptKey!, ct);
        foreach (var table in Tables)
            await ExecAsync(conn, tx, $"UPDATE {Ident(table.Name)} SET user_id = $2 WHERE user_id = $1", ct, merged, kept);
        if (hasMerged)
            await ExecAsync(conn, tx, "DELETE FROM users WHERE user_id = $1", ct, merged);
    }

    private async Task RekeyAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, Guid merged, Guid kept,
        string mergedStored, string keptStored, CancellationToken ct) {
        var from = UnprotectKey(mergedStored);
        var to = UnprotectKey(keptStored);
        if (from.Length == 0 || from == to)
            return;
        if (to.Length == 0) {
            await ExecAsync(conn, tx, "UPDATE users SET encryption_key = $2 WHERE user_id = $1", ct, kept, mergedStored);
            return;
        }

        List<(string Name, string Ciphertext)> blobs = [];
        await using (var sel = new NpgsqlCommand("SELECT name, ciphertext FROM blobs WHERE user_id = $1", conn, tx)) {
            sel.Parameters.AddWithValue(merged);
            await using var reader = await sel.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                blobs.Add((reader.GetString(0), reader.GetString(1)));
        }

        foreach (var (name, ciphertext) in blobs) {
            if (Rewrap(from, to, ciphertext) is { } rewrapped) {
                await ExecAsync(conn, tx, "UPDATE blobs SET ciphertext = $3 WHERE user_id = $1 AND name = $2", ct, merged, name, rewrapped);
            } else {
                logger.LogWarning("merges: blob {Name} of {UserId} does not decrypt under its own key, dropping it", name, merged);
                await ExecAsync(conn, tx, "DELETE FROM blobs WHERE user_id = $1 AND name = $2", ct, merged, name);
            }
        }

        await ExecAsync(conn, tx, "DELETE FROM sessions WHERE user_id = $1", ct, merged);
    }

    internal static string? Rewrap(string fromHexKey, string toHexKey, string ciphertext) {
        try {
            return BlobCrypto.Encrypt(toHexKey, BlobCrypto.Decrypt(fromHexKey, ciphertext));
        } catch (Exception ex) when (ex is CryptographicException or FormatException) {
            return null;
        }
    }

    internal static string CollisionSql(RemapTable table) =>
        $"DELETE FROM {Ident(table.Name)} m USING {Ident(table.Name)} k WHERE m.user_id = $1 AND k.user_id = $2"
        + string.Concat(table.UniqueKey.Select(c => $" AND m.{c} = k.{c}"));

    private static async Task<Dictionary<Guid, string>> LockUsersAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, Guid merged, Guid kept, CancellationToken ct) {
        Dictionary<Guid, string> keys = [];
        await using var cmd = new NpgsqlCommand(
            "SELECT user_id, encryption_key FROM users WHERE user_id = ANY($1) ORDER BY user_id FOR UPDATE", conn, tx);
        cmd.Parameters.AddWithValue(new[] { merged, kept });
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            keys[reader.GetGuid(0)] = reader.GetString(1);
        return keys;
    }

    private async Task<DateTimeOffset?> ReadWatermarkAsync(CancellationToken ct) {
        await using var cmd = source.CreateCommand("SELECT merged_at FROM identity_merge_watermark WHERE id");
        return await cmd.ExecuteScalarAsync(ct) is DateTime at ? new DateTimeOffset(at, TimeSpan.Zero) : null;
    }

    private static async Task ExecAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string sql, CancellationToken ct, params object[] args) {
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        foreach (var arg in args)
            cmd.Parameters.AddWithValue(arg);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private string UnprotectKey(string stored) {
        if (stored.Length == 0)
            return "";
        try {
            return _keyProtector.Unprotect(stored);
        } catch (CryptographicException ex) {
            logger.LogDebug(ex, "merges: encryption_key not protected, using stored value");
            return stored;
        }
    }

    private static string Ident(string table) => "\"" + table + "\"";
}
