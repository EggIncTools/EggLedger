using EggIdentity.Metrics;
using Npgsql;

namespace EggLedger.Web.Server.Sync.Admin;

public sealed class SpamLog(NpgsqlDataSource source) : IRequestAuditSink {
    public Task RecordAsync(AuditEntry entry, CancellationToken ct) =>
        RecordAsync(entry.Ip, entry.Method, entry.Path, "", entry.AtEpochSeconds);

    public async Task RecordAsync(string ip, string method, string path, string userAgent, long nowEpoch) {
        await using var cmd = source.CreateCommand(
            "INSERT INTO el_api_spam (ip, method, path, user_agent, first_seen, last_seen, hits) " +
            "VALUES ($1, $2, $3, $4, $5, $5, 1) " +
            "ON CONFLICT (ip, method, path) DO UPDATE SET " +
            "hits = el_api_spam.hits + 1, last_seen = $5, user_agent = $4");
        cmd.Parameters.AddWithValue(ip);
        cmd.Parameters.AddWithValue(method);
        cmd.Parameters.AddWithValue(path);
        cmd.Parameters.AddWithValue(userAgent);
        cmd.Parameters.AddWithValue(DateTimeOffset.FromUnixTimeSeconds(nowEpoch));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<SpamRow>> SnapshotAsync(int limit = 200) {
        var rows = new List<SpamRow>();
        await using var cmd = source.CreateCommand(
            "SELECT ip, method, path, user_agent, first_seen, last_seen, hits " +
            "FROM el_api_spam ORDER BY last_seen DESC LIMIT $1");
        cmd.Parameters.AddWithValue(limit);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) {
            rows.Add(new SpamRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetFieldValue<DateTimeOffset>(4).ToUnixTimeSeconds(),
                reader.GetFieldValue<DateTimeOffset>(5).ToUnixTimeSeconds(),
                reader.GetInt64(6)));
        }
        return rows;
    }

    public sealed record SpamRow(
        string Ip, string Method, string Path, string UserAgent,
        long FirstSeen, long LastSeen, long Hits);
}
