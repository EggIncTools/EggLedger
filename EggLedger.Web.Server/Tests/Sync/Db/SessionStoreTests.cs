using System.Net;
using EggIdentity.Auth;
using EggIdentity.Client;
using EggLedger.Web.Server.Sync.Db;
using EggLedger.Web.Server.Tests.Sync.Auth;
using Npgsql;

namespace EggLedger.Web.Server.Tests.Sync.Db;

public sealed class SessionStoreTests {
    private const string Schema = "eltest_sessionstore";

    private static SessionStore StoreFor(NpgsqlDataSource src, bool revoked) =>
        new(src, StubIdentity(revoked), new SessionRevocationCache(TimeProvider.System, TimeSpan.FromSeconds(30)), TimeProvider.System);

    private static IdentityApiClient StubIdentity(bool revoked) =>
        new(new HttpClient(new StubHttpMessageHandler(_ =>
            StubHttpMessageHandler.Json(HttpStatusCode.OK, revoked ? "true" : "false"))) {
            BaseAddress = new Uri("http://localhost:8090"),
        });

    [SkippableFact]
    public async Task LookupAsync_returns_user_id_not_discord_id() {
        TestDbUrl.SkipIfNotConfigured("session");

        await using var setupSrc = NpgsqlDataSource.Create(TestDbUrl.Value!);
        await CreateSchemaAsync(setupSrc);

        var scopedBuilder = new NpgsqlConnectionStringBuilder(TestDbUrl.Value!) { SearchPath = Schema };
        await using var src = NpgsqlDataSource.Create(scopedBuilder.ConnectionString);
        try {
            var userId = Guid.NewGuid();
            const string discordId = "99999999";
            const string token = "tok-abc";
            var expiresAt = DateTimeOffset.UtcNow.AddDays(1);

            await Exec(src, $"""
                INSERT INTO users (user_id, created_at) VALUES ('{userId}', to_timestamp(0));
                INSERT INTO identities (user_id, provider, subject) VALUES ('{userId}', 'discord', '{discordId}');
                INSERT INTO sessions (token_hash, user_id, expires_at)
                VALUES ('{TokenHash.Of(token)}', '{userId}', '{expiresAt:O}');
                """);

            var store = StoreFor(src, revoked: false);
            var (found, returnedId, returnedExpiresAt) = await store.LookupAsync(token, CancellationToken.None);

            Assert.True(found);
            Assert.Equal(userId.ToString(), returnedId);
            Assert.NotEqual(discordId, returnedId);
            Assert.Equal(expiresAt.ToUnixTimeSeconds(), returnedExpiresAt);
        } finally {
            await DropSchemaAsync(setupSrc);
        }
    }

    [SkippableFact]
    public async Task LookupAsync_returns_not_found_when_identity_reports_session_revoked() {
        TestDbUrl.SkipIfNotConfigured("session");

        await using var setupSrc = NpgsqlDataSource.Create(TestDbUrl.Value!);
        await CreateSchemaAsync(setupSrc);

        var scopedBuilder = new NpgsqlConnectionStringBuilder(TestDbUrl.Value!) { SearchPath = Schema };
        await using var src = NpgsqlDataSource.Create(scopedBuilder.ConnectionString);
        try {
            var userId = Guid.NewGuid();
            const string discordId = "99999999";
            const string token = "tok-revoked";
            var expiresAt = DateTimeOffset.UtcNow.AddDays(1);

            await Exec(src, $"""
                INSERT INTO users (user_id, created_at) VALUES ('{userId}', to_timestamp(0));
                INSERT INTO identities (user_id, provider, subject) VALUES ('{userId}', 'discord', '{discordId}');
                INSERT INTO sessions (token_hash, user_id, expires_at)
                VALUES ('{TokenHash.Of(token)}', '{userId}', '{expiresAt:O}');
                """);

            var store = StoreFor(src, revoked: true);
            var (found, returnedId, returnedExpiresAt) = await store.LookupAsync(token, CancellationToken.None);

            Assert.False(found);
            Assert.Equal(string.Empty, returnedId);
            Assert.Equal(0, returnedExpiresAt);
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
        await ApplyMigrationAsync(src, "10_identities_user_id_cascade.up.sql");
        await ApplyMigrationAsync(src, "15_inflight_mission.up.sql");
        await ApplyMigrationAsync(src, "16_mission_fuel.up.sql");
        await ApplyMigrationAsync(src, "17_pinned_reports.up.sql");
        await ApplyMigrationAsync(src, "18_session_token_hash.up.sql");
        await ApplyMigrationAsync(src, "19_users_modern_shape.up.sql");
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
