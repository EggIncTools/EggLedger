using EggIdentity.Auth;
using EggIdentity.Client;
using EggLedger.Web.Server.Sync;
using Microsoft.AspNetCore.Authentication;
using Npgsql;

namespace EggLedger.Web.Server.Auth;





public static class AuthentikAuth {


    public static bool AddIfConfigured(AuthenticationBuilder builder, AppConfig cfg, IdentityApiClient _, NpgsqlDataSource source) {
        if (string.IsNullOrEmpty(cfg.AuthentikAuthority)) {
            return false;
        }

        return AuthentikAspNetAuth.AddIfConfigured(builder, new AuthentikAspNetAuthOptions {
            CookieScheme = AuthScheme.Cookie,
            Authority = cfg.AuthentikAuthority,
            ClientId = cfg.AuthentikClientId,
            ClientSecret = cfg.AuthentikClientSecret,
            CallbackPath = "/auth",
            UserIdClaim = AuthScheme.UserIdClaim,
            RoleClaim = AuthScheme.RoleClaim,
            DiscordIdClaim = AuthScheme.DiscordIdClaim,
            OnResolved = async (result, identity, ctx) => {
                identity.AddClaim(new System.Security.Claims.Claim(SessionClaims.Role, result.Role));

                await using var cmd = source.CreateCommand(
                    "INSERT INTO users (user_id, created_at) VALUES ($1, $2) ON CONFLICT (user_id) DO NOTHING");
                cmd.Parameters.AddWithValue(result.UserId);
                cmd.Parameters.AddWithValue(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                await cmd.ExecuteNonQueryAsync(ctx.RequestAborted);
            },
        });
    }
}
