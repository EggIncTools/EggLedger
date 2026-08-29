using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using EggIdentity.Auth;
using EggIdentity.Client;
using EggLedger.Web.Server.Sync;
using EggLedger.Web.Server.Sync.Auth;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace EggLedger.Web.Server.Tests.Sync.Auth;

public sealed class AuthEndpointsTests {
    private const string Schema = "eltest_authendpoints";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [SkippableFact]
    public async Task EnsureEncryptionKeyAsync_returns_existing_key_for_discord_linked_user() {
        TestDbUrl.SkipIfNotConfigured("auth");

        await using var setupSrc = NpgsqlDataSource.Create(TestDbUrl.Value!);
        await CreateSchemaAsync(setupSrc);

        var scopedBuilder = new NpgsqlConnectionStringBuilder(TestDbUrl.Value!) { SearchPath = Schema };
        await using var src = NpgsqlDataSource.Create(scopedBuilder.ConnectionString);
        try {
            var protector = new EphemeralDataProtectionProvider();
            var identity = new IdentityApiClient(new HttpClient(new StubHttpMessageHandler(_ =>
                StubHttpMessageHandler.Json(HttpStatusCode.OK, "{}"))) { BaseAddress = new Uri("http://localhost:8090") });
            var endpoints = new AuthEndpoints(src, protector, identity, NullLogger<AuthEndpoints>.Instance, AppConfig.FromEnv(_ => null));

            var userId = Guid.NewGuid();
            var protectedKey = protector.CreateProtector("EggLedger.EncryptionKey").Protect("existing-key-value");
            await using (var seed = src.CreateCommand(
                "INSERT INTO users (user_id, discord_id, created_at, encryption_key) VALUES ($1, $2, $3, $4)")) {
                seed.Parameters.AddWithValue(userId);
                seed.Parameters.AddWithValue("99999");
                seed.Parameters.AddWithValue(1000L);
                seed.Parameters.AddWithValue(protectedKey);
                await seed.ExecuteNonQueryAsync();
            }

            var key = await endpoints.EnsureEncryptionKeyAsync(userId);

            Assert.Equal("existing-key-value", key);
        } finally {
            await DropSchemaAsync(setupSrc);
        }
    }





    [SkippableFact]
    public async Task EnsureEncryptionKeyAsync_generates_and_persists_key_for_non_discord_user() {
        TestDbUrl.SkipIfNotConfigured("auth");

        await using var setupSrc = NpgsqlDataSource.Create(TestDbUrl.Value!);
        await CreateSchemaAsync(setupSrc);

        var scopedBuilder = new NpgsqlConnectionStringBuilder(TestDbUrl.Value!) { SearchPath = Schema };
        await using var src = NpgsqlDataSource.Create(scopedBuilder.ConnectionString);
        try {
            var identity = new IdentityApiClient(new HttpClient(new StubHttpMessageHandler(_ =>
                StubHttpMessageHandler.Json(HttpStatusCode.OK, "{}"))) { BaseAddress = new Uri("http://localhost:8090") });
            var endpoints = new AuthEndpoints(src, new EphemeralDataProtectionProvider(), identity, NullLogger<AuthEndpoints>.Instance, AppConfig.FromEnv(_ => null));

            var userId = Guid.NewGuid();
            await using (var seed = src.CreateCommand(
                "INSERT INTO users (user_id, discord_id, created_at) VALUES ($1, NULL, $2)")) {
                seed.Parameters.AddWithValue(userId);
                seed.Parameters.AddWithValue(2000L);
                await seed.ExecuteNonQueryAsync();
            }

            var key = await endpoints.EnsureEncryptionKeyAsync(userId);

            Assert.False(string.IsNullOrEmpty(key));

            await using var cmd = src.CreateCommand("SELECT encryption_key FROM users WHERE user_id = $1");
            cmd.Parameters.AddWithValue(userId);
            var stored = Assert.IsType<string>(await cmd.ExecuteScalarAsync());
            Assert.False(string.IsNullOrEmpty(stored));
        } finally {
            await DropSchemaAsync(setupSrc);
        }
    }

    [SkippableFact]
    public async Task SessionFromLogin_Unauthenticated_Returns401() {
        TestDbUrl.SkipIfNotConfigured("auth");

        await using var setupSrc = NpgsqlDataSource.Create(TestDbUrl.Value!);
        await CreateSchemaAsync(setupSrc);

        var scopedBuilder = new NpgsqlConnectionStringBuilder(TestDbUrl.Value!) { SearchPath = Schema };
        await using var src = NpgsqlDataSource.Create(scopedBuilder.ConnectionString);
        try {
            var identity = new IdentityApiClient(new HttpClient(new StubHttpMessageHandler(_ =>
                StubHttpMessageHandler.Json(HttpStatusCode.OK, "{}"))) { BaseAddress = new Uri("http://localhost:8090") });
            var endpoints = new AuthEndpoints(src, new EphemeralDataProtectionProvider(), identity, NullLogger<AuthEndpoints>.Instance, AppConfig.FromEnv(_ => null));

            var ctx = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) };
            ctx.Response.Body = new MemoryStream();

            await endpoints.SessionFromLogin(ctx);

            Assert.Equal(StatusCodes.Status401Unauthorized, ctx.Response.StatusCode);
        } finally {
            await DropSchemaAsync(setupSrc);
        }
    }



    [SkippableFact]
    public async Task SessionFromLogin_AuthenticatedNoDiscordId_CreatesSessionWithNullDiscordId() {
        TestDbUrl.SkipIfNotConfigured("auth");

        await using var setupSrc = NpgsqlDataSource.Create(TestDbUrl.Value!);
        await CreateSchemaAsync(setupSrc);

        var scopedBuilder = new NpgsqlConnectionStringBuilder(TestDbUrl.Value!) { SearchPath = Schema };
        await using var src = NpgsqlDataSource.Create(scopedBuilder.ConnectionString);
        try {
            var identity = new IdentityApiClient(new HttpClient(new StubHttpMessageHandler(_ =>
                StubHttpMessageHandler.Json(HttpStatusCode.OK, "{}"))) { BaseAddress = new Uri("http://localhost:8090") });
            var endpoints = new AuthEndpoints(src, new EphemeralDataProtectionProvider(), identity, NullLogger<AuthEndpoints>.Instance, AppConfig.FromEnv(_ => null));

            var userId = Guid.NewGuid();
            await using (var seed = src.CreateCommand(
                "INSERT INTO users (user_id, discord_id, created_at) VALUES ($1, NULL, $2)")) {
                seed.Parameters.AddWithValue(userId);
                seed.Parameters.AddWithValue(3000L);
                await seed.ExecuteNonQueryAsync();
            }

            var claims = new List<Claim> {
                new(Server.Auth.AuthScheme.UserIdClaim, userId.ToString()),
                new(ClaimTypes.Name, "authentik-user"),
            };
            var ctx = new DefaultHttpContext {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, Server.Auth.AuthScheme.Cookie)),
            };
            ctx.Response.Body = new MemoryStream();

            await endpoints.SessionFromLogin(ctx);

            Assert.Equal(StatusCodes.Status200OK, ctx.Response.StatusCode);
            ctx.Response.Body.Seek(0, SeekOrigin.Begin);
            var body = await JsonSerializer.DeserializeAsync<PollResponse>(ctx.Response.Body, JsonOptions);
            Assert.NotNull(body);
            Assert.False(string.IsNullOrEmpty(body!.Token));

            await using var cmd = src.CreateCommand(
                "SELECT user_id, discord_id FROM sessions WHERE token = $1");
            cmd.Parameters.AddWithValue(body.Token);
            await using var reader = await cmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(userId, reader.GetGuid(0));
            Assert.True(reader.IsDBNull(1));
        } finally {
            await DropSchemaAsync(setupSrc);
        }
    }



    [SkippableFact]
    public async Task SessionFromLogin_AuthenticatedWithDiscordId_CreatesSessionWithDiscordId() {
        TestDbUrl.SkipIfNotConfigured("auth");

        await using var setupSrc = NpgsqlDataSource.Create(TestDbUrl.Value!);
        await CreateSchemaAsync(setupSrc);

        var scopedBuilder = new NpgsqlConnectionStringBuilder(TestDbUrl.Value!) { SearchPath = Schema };
        await using var src = NpgsqlDataSource.Create(scopedBuilder.ConnectionString);
        try {
            var identity = new IdentityApiClient(new HttpClient(new StubHttpMessageHandler(_ =>
                StubHttpMessageHandler.Json(HttpStatusCode.OK, "{}"))) { BaseAddress = new Uri("http://localhost:8090") });
            var endpoints = new AuthEndpoints(src, new EphemeralDataProtectionProvider(), identity, NullLogger<AuthEndpoints>.Instance, AppConfig.FromEnv(_ => null));

            var userId = Guid.NewGuid();
            await using (var seed = src.CreateCommand(
                "INSERT INTO users (user_id, discord_id, created_at, avatar_url) VALUES ($1, $2, $3, $4)")) {
                seed.Parameters.AddWithValue(userId);
                seed.Parameters.AddWithValue("54321");
                seed.Parameters.AddWithValue(4000L);
                seed.Parameters.AddWithValue("http://example.com/avatar.png");
                await seed.ExecuteNonQueryAsync();
            }

            var claims = new List<Claim> {
                new(Server.Auth.AuthScheme.UserIdClaim, userId.ToString()),
                new(Server.Auth.AuthScheme.DiscordIdClaim, "54321"),
                new(ClaimTypes.Name, "discord-user"),
            };
            var ctx = new DefaultHttpContext {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, Server.Auth.AuthScheme.Cookie)),
            };
            ctx.Response.Body = new MemoryStream();

            await endpoints.SessionFromLogin(ctx);

            Assert.Equal(StatusCodes.Status200OK, ctx.Response.StatusCode);
            ctx.Response.Body.Seek(0, SeekOrigin.Begin);
            var body = await JsonSerializer.DeserializeAsync<PollResponse>(ctx.Response.Body, JsonOptions);
            Assert.NotNull(body);
            Assert.Equal("http://example.com/avatar.png", body!.AvatarUrl);

            await using var cmd = src.CreateCommand(
                "SELECT user_id, discord_id FROM sessions WHERE token = $1");
            cmd.Parameters.AddWithValue(body.Token);
            await using var reader = await cmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(userId, reader.GetGuid(0));
            Assert.Equal("54321", reader.GetString(1));
        } finally {
            await DropSchemaAsync(setupSrc);
        }
    }

    [SkippableFact]
    public async Task DeleteSession_calls_identity_revoke() {
        TestDbUrl.SkipIfNotConfigured("auth");

        await using var setupSrc = NpgsqlDataSource.Create(TestDbUrl.Value!);
        await CreateSchemaAsync(setupSrc);

        var scopedBuilder = new NpgsqlConnectionStringBuilder(TestDbUrl.Value!) { SearchPath = Schema };
        await using var src = NpgsqlDataSource.Create(scopedBuilder.ConnectionString);
        try {
            const string token = "tok-to-revoke";
            HttpRequestMessage? revokeRequest = null;
            var handler = new StubHttpMessageHandler(req => {
                revokeRequest = req;
                return StubHttpMessageHandler.Json(HttpStatusCode.OK, "{}");
            });
            var identity = new IdentityApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8090") });
            var endpoints = new AuthEndpoints(src, new EphemeralDataProtectionProvider(), identity, NullLogger<AuthEndpoints>.Instance, AppConfig.FromEnv(_ => null));

            var userId = Guid.NewGuid();
            await using (var seed = src.CreateCommand(
                "INSERT INTO users (user_id, discord_id, created_at) VALUES ($1, NULL, $2);" +
                "INSERT INTO sessions (token, discord_id, user_id, expires_at) VALUES ($3, NULL, $1, $4)")) {
                seed.Parameters.AddWithValue(userId);
                seed.Parameters.AddWithValue(3000L);
                seed.Parameters.AddWithValue(token);
                seed.Parameters.AddWithValue(DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds());
                await seed.ExecuteNonQueryAsync();
            }

            var ctx = new DefaultHttpContext();
            ctx.Request.Headers.Authorization = $"Bearer {token}";
            ctx.Response.Body = new MemoryStream();

            await endpoints.DeleteSession(ctx);

            Assert.Equal(StatusCodes.Status204NoContent, ctx.Response.StatusCode);
            Assert.NotNull(revokeRequest);
            Assert.Equal("/identity/revoke-session", revokeRequest!.RequestUri!.AbsolutePath);
            var body = await revokeRequest.Content!.ReadAsStringAsync();
            Assert.Contains(token, body);

            await using var cmd = src.CreateCommand("SELECT COUNT(*) FROM sessions WHERE token = $1");
            cmd.Parameters.AddWithValue(token);
            Assert.Equal(0L, await cmd.ExecuteScalarAsync());
        } finally {
            await DropSchemaAsync(setupSrc);
        }
    }





    private static async Task CreateSchemaAsync(NpgsqlDataSource src) {
        await Exec(src, $"DROP SCHEMA IF EXISTS {Schema} CASCADE; CREATE SCHEMA {Schema}; SET search_path TO {Schema};");
        await ApplyMigrationAsync(src, "1_initial_schema.up.sql");
        await ApplyMigrationAsync(src, "2_add_user_profile.up.sql");
        await ApplyMigrationAsync(src, "3_add_encryption_key.up.sql");
        await ApplyMigrationAsync(src, "4_eggledger_storage.up.sql");
        await ApplyMigrationAsync(src, "5_data_protection_keys.up.sql");
        await ApplyMigrationAsync(src, "6_api_spam_log.up.sql");
        await ApplyMigrationAsync(src, "7_cascade_eggledger_storage.up.sql");
        await ApplyMigrationAsync(src, "8_identities.up.sql");
        await ApplyMigrationAsync(src, "9_identity_user_id_cascade.up.sql");
    }

    private static async Task ApplyMigrationAsync(NpgsqlDataSource src, string fileName) {
        var sqlPath = Path.Combine(AppContext.BaseDirectory, "Migrations", fileName);
        var sql = await File.ReadAllTextAsync(sqlPath);
        await Exec(src, $"SET search_path TO {Schema}; {sql}");
    }

    private static async Task DropSchemaAsync(NpgsqlDataSource src) =>
        await Exec(src, $"DROP SCHEMA IF EXISTS {Schema} CASCADE;");

    private static async Task Exec(NpgsqlDataSource src, string sql) {
        await using var cmd = src.CreateCommand(sql);
        await cmd.ExecuteNonQueryAsync();
    }
}
