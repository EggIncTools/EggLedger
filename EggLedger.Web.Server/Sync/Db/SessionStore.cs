using EggIdentity.Auth;
using EggIdentity.Client;
using Npgsql;

namespace EggLedger.Web.Server.Sync.Db;

public sealed class SessionStore(
    NpgsqlDataSource source,
    IdentityApiClient identity,
    SessionRevocationCache revocations,
    TimeProvider clock) : ISessionStore {

    public async Task<(bool Found, string DiscordId, long ExpiresAt)> LookupAsync(string token, CancellationToken ct) {
        var hash = TokenHash.Of(token);
        Guid userId;
        DateTimeOffset expiresAt;

        await using (var cmd = source.CreateCommand(
            "SELECT user_id, expires_at FROM sessions WHERE token_hash = $1")) {
            cmd.Parameters.AddWithValue(hash);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                return (false, string.Empty, 0);
            userId = reader.GetGuid(0);
            expiresAt = reader.GetFieldValue<DateTimeOffset>(1);
        }

        if (expiresAt <= clock.GetUtcNow())
            return (false, string.Empty, 0);

        var revoked = await revocations.IsRevokedAsync(hash, c => identity.IsRevokedAsync(hash, c), ct);
        if (revoked)
            return (false, string.Empty, 0);

        return (true, userId.ToString(), expiresAt.ToUnixTimeSeconds());
    }

    public async Task TouchAsync(string token, long newExpiresAt, CancellationToken ct) {
        await using var cmd = source.CreateCommand(
            "UPDATE sessions SET expires_at = $1 WHERE token_hash = $2");
        cmd.Parameters.AddWithValue(DateTimeOffset.FromUnixTimeSeconds(newExpiresAt));
        cmd.Parameters.AddWithValue(TokenHash.Of(token));
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
