using EggIdentity.Client;
using EggLedger.Web.Components.Admin;
using EggLedger.Web.Services;
using Npgsql;

namespace EggLedger.Web.Server.Sync.Admin;

public sealed class AdminDataService(NpgsqlDataSource source, IdentityApiClient identity) : IAdminData {
    private const string UsersSql =
        "WITH mission_agg AS (" +
        "  SELECT user_id, COUNT(*) AS cnt, SUM(pg_column_size(m.*)) AS bytes FROM el_mission m GROUP BY user_id" +
        "), backup_agg AS (" +
        "  SELECT user_id, COUNT(*) AS cnt, SUM(pg_column_size(bk.*)) AS bytes FROM el_backup bk GROUP BY user_id" +
        "), drops_agg AS (" +
        "  SELECT user_id, SUM(pg_column_size(d.*)) AS bytes FROM el_artifact_drops d GROUP BY user_id" +
        "), settings_agg AS (" +
        "  SELECT user_id, SUM(pg_column_size(s.*)) AS bytes FROM el_settings s GROUP BY user_id" +
        "), reports_agg AS (" +
        "  SELECT user_id, COUNT(*) AS cnt, SUM(pg_column_size(r.*)) AS bytes FROM el_reports r GROUP BY user_id" +
        "), groups_agg AS (" +
        "  SELECT user_id, SUM(pg_column_size(g.*)) AS bytes FROM el_report_groups g GROUP BY user_id" +
        "), blobs_agg AS (" +
        "  SELECT user_id, SUM(pg_column_size(b.*)) AS bytes FROM blobs b GROUP BY user_id" +
        "), session_agg AS (" +
        "  SELECT user_id, MAX(expires_at) AS last_session FROM sessions GROUP BY user_id" +
        ") " +
        "SELECT u.discord_id, u.username, u.avatar_url, u.user_id, " +
        "COALESCE(mission_agg.cnt, 0), COALESCE(backup_agg.cnt, 0), COALESCE(reports_agg.cnt, 0), " +
        "COALESCE(mission_agg.bytes, 0) + COALESCE(backup_agg.bytes, 0) + COALESCE(drops_agg.bytes, 0) + " +
        "COALESCE(settings_agg.bytes, 0) + COALESCE(reports_agg.bytes, 0) + COALESCE(groups_agg.bytes, 0) + " +
        "COALESCE(blobs_agg.bytes, 0), " +
        "session_agg.last_session " +
        "FROM users u " +
        "LEFT JOIN mission_agg ON mission_agg.user_id = u.user_id " +
        "LEFT JOIN backup_agg ON backup_agg.user_id = u.user_id " +
        "LEFT JOIN drops_agg ON drops_agg.user_id = u.user_id " +
        "LEFT JOIN settings_agg ON settings_agg.user_id = u.user_id " +
        "LEFT JOIN reports_agg ON reports_agg.user_id = u.user_id " +
        "LEFT JOIN groups_agg ON groups_agg.user_id = u.user_id " +
        "LEFT JOIN blobs_agg ON blobs_agg.user_id = u.user_id " +
        "LEFT JOIN session_agg ON session_agg.user_id = u.user_id " +
        "ORDER BY u.username";

    public async Task<IReadOnlyList<AdminUser>> GetUsersAsync(CancellationToken ct = default) {
        var roleByUserId = (await identity.ListAdminUsersAsync(ct))
            .ToDictionary(u => u.UserId, u => u.Role);

        var users = new List<AdminUser>();
        await using var cmd = source.CreateCommand(UsersSql);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) {
            var rowUserId = reader.GetGuid(3);
            users.Add(new AdminUser(
                rowUserId,
                reader.IsDBNull(0) ? null : reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt64(4),
                reader.GetInt64(5),
                reader.GetInt64(6),
                reader.GetInt64(7),
                reader.IsDBNull(8) ? (long?)null : reader.GetInt64(8),
                roleByUserId.GetValueOrDefault(rowUserId) == "admin"));
        }
        return users;
    }

    public Task<bool> DeleteUserAsync(Guid userId, CancellationToken ct = default) =>
        UserDataDeletion.DeleteUserAsync(source, userId, ct);
}
