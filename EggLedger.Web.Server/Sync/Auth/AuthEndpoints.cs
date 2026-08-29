using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EggIdentity.Auth;
using EggIdentity.Client;
using EggIdentity.Contract;
using EggLedger.Web.Server.Sync;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace EggLedger.Web.Server.Sync.Auth;




public sealed record PairInitResponse(
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("state")] string State);

public sealed record PollResponse(
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("avatarUrl")] string AvatarUrl,
    [property: JsonPropertyName("encryptionKey")] string EncryptionKey);

public sealed class AuthEndpoints(NpgsqlDataSource source, IDataProtectionProvider dataProtection, IdentityApiClient identity, ILogger<AuthEndpoints> logger, AppConfig cfg, SessionCookieOptions? eggIdentitySession = null) {
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly string[] ElUserTables = [
        "el_mission", "el_backup", "el_artifact_drops", "el_settings", "el_reports", "el_report_groups",
    ];
    private readonly IDataProtector _keyProtector = dataProtection.CreateProtector("EggLedger.EncryptionKey");
    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();



    private string UnprotectKey(string stored) {
        try {
            return _keyProtector.Unprotect(stored);
        } catch (CryptographicException) {
            return stored;
        }
    }

    private static async Task WriteTextAsync(HttpContext ctx, int statusCode, string text) {
        ctx.Response.StatusCode = statusCode;
        ctx.Response.ContentType = "text/plain; charset=utf-8";
        await ctx.Response.Body.WriteAsync(Encoding.UTF8.GetBytes(text), ctx.RequestAborted);
    }

    private static async Task WriteJsonAsync<T>(HttpContext ctx, T value) {
        ctx.Response.StatusCode = StatusCodes.Status200OK;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(ctx.Response.Body, value, Json, ctx.RequestAborted);
    }

    public async Task<RedeemLoginCodeResponse> RedeemAndSignInAsync(HttpContext ctx, string code, CancellationToken ct) {
        var result = await identity.RedeemAsync(code, ct);

        await UpsertLocalUserAsync(result.UserId, result.DiscordId, result.Username, result.Avatar, ct);

        if (eggIdentitySession is null) {
            var claims = new List<Claim> {
                new(ClaimTypes.Name, result.Username),
                new(Server.Auth.AuthScheme.UserIdClaim, result.UserId.ToString()),
                new(Server.Auth.AuthScheme.RoleClaim, result.Role),
                new(SessionClaims.Role, result.Role),
            };
            if (!string.IsNullOrEmpty(result.DiscordId)) {
                claims.Add(new Claim(Server.Auth.AuthScheme.DiscordIdClaim, result.DiscordId));
            }
            var claimsIdentity = new ClaimsIdentity(claims, Server.Auth.AuthScheme.Cookie);
            await ctx.SignInAsync(
                Server.Auth.AuthScheme.Cookie,
                new ClaimsPrincipal(claimsIdentity),
                new AuthenticationProperties { IsPersistent = true });
        }

        return result;
    }

    public async Task Logout(HttpContext ctx) {
        await ctx.SignOutAsync(Server.Auth.AuthScheme.Cookie);

        if (eggIdentitySession is null) {
            ctx.Response.Redirect("/");
            return;
        }

        var referer = ctx.Request.Headers.Referer.ToString();
        var returnUrl = string.IsNullOrEmpty(referer) ? cfg.PublicBaseUrl : referer;
        ctx.Response.Redirect($"{cfg.IdentityWidgetUrl.TrimEnd('/')}/auth/logout?returnUrl={Uri.EscapeDataString(returnUrl)}");
    }



    private async Task UpsertLocalUserAsync(Guid userId, string? discordId, string? username, string? avatar, CancellationToken ct) {
        await using (var u = source.CreateCommand(
            "INSERT INTO users (user_id, discord_id, created_at, username, avatar_url) VALUES ($1,$2,$3,$4,$5) " +
            "ON CONFLICT (user_id) DO UPDATE SET username = EXCLUDED.username, avatar_url = EXCLUDED.avatar_url")) {
            u.Parameters.AddWithValue(userId);
            if (string.IsNullOrEmpty(discordId)) {
                u.Parameters.AddWithValue(DBNull.Value);
            } else {
                u.Parameters.AddWithValue(discordId);
            }
            u.Parameters.AddWithValue(Now());
            u.Parameters.AddWithValue(username ?? "");
            u.Parameters.AddWithValue(avatar ?? "");
            await u.ExecuteNonQueryAsync(ct);
        }

        await EnsureEncryptionKeyAsync(userId);
    }

    private async Task<bool> StateIsPending(string state, CancellationToken ct) {
        await using var cmd = source.CreateCommand(
            "SELECT 1 FROM pending_auth WHERE state = $1 AND expires_at > $2");
        cmd.Parameters.AddWithValue(state);
        cmd.Parameters.AddWithValue(Now());
        try {
            return await cmd.ExecuteScalarAsync(ct) is not null;
        } catch (Exception ex) {
            logger.LogWarning(ex, "auth: failed to check pending state");
            return false;
        }
    }



    internal async Task<string> EnsureEncryptionKeyAsync(Guid userId) {
        var encKey = "";
        await using (var sel = source.CreateCommand("SELECT encryption_key FROM users WHERE user_id = $1")) {
            sel.Parameters.AddWithValue(userId);

            try {
                var result = await sel.ExecuteScalarAsync();
                if (result is string { Length: > 0 } stored)
                    encKey = UnprotectKey(stored);
            } catch (Exception ex) {
                logger.LogWarning(ex, "auth: failed to read encryption_key for {UserId}", userId);
            }
        }
        if (string.IsNullOrEmpty(encKey)) {
            encKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            await using var upd = source.CreateCommand("UPDATE users SET encryption_key = $1 WHERE user_id = $2");
            upd.Parameters.AddWithValue(_keyProtector.Protect(encKey));
            upd.Parameters.AddWithValue(userId);
            try { await upd.ExecuteNonQueryAsync(); } catch (Exception ex) { logger.LogWarning(ex, "auth: failed to store encryption_key for {UserId}", userId); }
        }
        return encKey;
    }

    public async Task Poll(HttpContext ctx, string state) {
        string? token = null;
        long expiresAt = 0;
        string username = "", avatarUrl = "", encryptionKey = "";
        var found = false;

        try {
            await using var cmd = source.CreateCommand(
                "SELECT session_token, expires_at, username, avatar_url, encryption_key FROM pending_auth WHERE state = $1");
            cmd.Parameters.AddWithValue(state);
            await using var reader = await cmd.ExecuteReaderAsync(ctx.RequestAborted);
            if (await reader.ReadAsync(ctx.RequestAborted)) {
                found = true;
                token = reader.IsDBNull(0) ? null : reader.GetString(0);
                expiresAt = reader.GetInt64(1);
                username = reader.IsDBNull(2) ? "" : reader.GetString(2);
                avatarUrl = reader.IsDBNull(3) ? "" : reader.GetString(3);
                encryptionKey = reader.IsDBNull(4) ? "" : reader.GetString(4);
            }
        } catch (Exception ex) {
            logger.LogWarning(ex, "auth: poll query failed for state {State}", state);
        }

        if (!found || Now() > expiresAt) {
            await WriteTextAsync(ctx, StatusCodes.Status404NotFound, "not found\n");
            return;
        }
        if (token is null) {
            ctx.Response.StatusCode = StatusCodes.Status202Accepted;
            return;
        }
        await using (var del = source.CreateCommand("DELETE FROM pending_auth WHERE state = $1")) {
            del.Parameters.AddWithValue(state);
            try { await del.ExecuteNonQueryAsync(ctx.RequestAborted); } catch (Exception ex) { logger.LogWarning(ex, "auth: failed to delete pending_auth row for state {State}", state); }
        }
        var plainKey = string.IsNullOrEmpty(encryptionKey) ? "" : UnprotectKey(encryptionKey);
        await WriteJsonAsync(ctx, new PollResponse(token, username, avatarUrl, plainKey));
    }




    public async Task SessionFromLogin(HttpContext ctx) {
        var token = eggIdentitySession is not null ? ctx.Request.Cookies[eggIdentitySession.CookieName] : null;
        if (string.IsNullOrEmpty(token)) {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var profile = await identity.GetProfileAsync(token, ctx.RequestAborted);
        if (profile is null) {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var discordId = profile.Identities.FirstOrDefault(i => i.Provider == "discord")?.Subject;
        await UpsertLocalUserAsync(profile.UserId, discordId, profile.Username, profile.Avatar, ctx.RequestAborted);

        var poll = await MintSessionAsync(profile.UserId, profile.Username, discordId, ctx.RequestAborted);
        await WriteJsonAsync(ctx, poll);
    }

    public async Task<PollResponse> MintSessionAsync(Guid userId, string username, string? discordId, CancellationToken ct) {
        var encKey = await EnsureEncryptionKeyAsync(userId);
        var token = Guid.NewGuid().ToString("N");

        await using (var ins = source.CreateCommand(
            "INSERT INTO sessions (token, discord_id, user_id, expires_at) VALUES ($1, $2, $3, $4)")) {
            ins.Parameters.AddWithValue(token);
            if (string.IsNullOrEmpty(discordId)) {
                ins.Parameters.AddWithValue(DBNull.Value);
            } else {
                ins.Parameters.AddWithValue(discordId);
            }
            ins.Parameters.AddWithValue(userId);
            ins.Parameters.AddWithValue(Now() + 30L * 24 * 3600);
            await ins.ExecuteNonQueryAsync(ct);
        }

        var avatarUrl = await UserAvatarUrlAsync(userId, ct);
        return new PollResponse(token, username, avatarUrl, encKey);
    }

    public async Task PairBegin(HttpContext ctx) {
        var state = Guid.NewGuid().ToString("N");
        await using var cmd = source.CreateCommand(
            "INSERT INTO pending_auth (state, expires_at) VALUES ($1, $2) ON CONFLICT (state) DO UPDATE SET expires_at = EXCLUDED.expires_at");
        cmd.Parameters.AddWithValue(state);
        cmd.Parameters.AddWithValue(Now() + 600);
        try { await cmd.ExecuteNonQueryAsync(ctx.RequestAborted); } catch (Exception ex) { logger.LogWarning(ex, "auth: failed to seed pending_auth row"); }

        var url = new Uri(new Uri(cfg.PublicBaseUrl), $"/pair?state={state}").ToString();
        await WriteJsonAsync(ctx, new PairInitResponse(url, state));
    }

    public async Task<bool> CompletePairingAsync(ClaimsPrincipal user, string state, CancellationToken ct) {
        if (!await StateIsPending(state, ct)) {
            return false;
        }

        var userIdClaim = user.FindFirst(Server.Auth.AuthScheme.UserIdClaim)?.Value;
        if (!Guid.TryParse(userIdClaim, out var userId)) {
            return false;
        }

        var username = user.Identity?.Name ?? "";
        var discordIdClaim = user.FindFirst(Server.Auth.AuthScheme.DiscordIdClaim)?.Value;
        var poll = await MintSessionAsync(userId, username, discordIdClaim, ct);

        await using var pend = source.CreateCommand(
            "UPDATE pending_auth SET session_token = $1, username = $2, avatar_url = $3, encryption_key = $4 WHERE state = $5");
        pend.Parameters.AddWithValue(poll.Token);
        pend.Parameters.AddWithValue(poll.Username);
        pend.Parameters.AddWithValue(poll.AvatarUrl);
        pend.Parameters.AddWithValue(_keyProtector.Protect(poll.EncryptionKey));
        pend.Parameters.AddWithValue(state);
        await pend.ExecuteNonQueryAsync(ct);
        return true;
    }

    private async Task<string> UserAvatarUrlAsync(Guid userId, CancellationToken ct) {
        await using var cmd = source.CreateCommand("SELECT avatar_url FROM users WHERE user_id = $1");
        cmd.Parameters.AddWithValue(userId);
        try {
            var result = await cmd.ExecuteScalarAsync(ct);
            return result as string ?? "";
        } catch (Exception ex) {
            logger.LogWarning(ex, "auth: failed to read avatar_url for {UserId}", userId);
            return "";
        }
    }

    public async Task DeleteSession(HttpContext ctx) {
        var authHeader = ctx.Request.Headers["Authorization"].ToString();
        var token = authHeader.StartsWith("Bearer ", StringComparison.Ordinal)
            ? authHeader["Bearer ".Length..]
            : "";
        if (token.Length > 0) {
            await using var cmd = source.CreateCommand("DELETE FROM sessions WHERE token = $1");
            cmd.Parameters.AddWithValue(token);
            try { await cmd.ExecuteNonQueryAsync(ctx.RequestAborted); } catch (Exception ex) { logger.LogWarning(ex, "auth: failed to delete session"); }
            try { await identity.RevokeSessionAsync(token, ctx.RequestAborted); } catch (Exception ex) { logger.LogWarning(ex, "auth: failed to revoke session"); }
        }
        ctx.Response.StatusCode = StatusCodes.Status204NoContent;
    }
}
