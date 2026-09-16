using System.Net;
using System.Text.Json;
using EggIdentity.Client;
using EggIdentity.Contract;
using EggLedger.Web.Server.Sync.Admin;
using EggLedger.Web.Server.Sync.Auth;
using EggLedger.Web.Server.Tests.Sync.Auth;
using Microsoft.AspNetCore.Http;
using Npgsql;

namespace EggLedger.Web.Server.Tests.Sync.Admin;

public sealed class AdminEndpointsTests {
    private const string Schema = "eltest_admin";

    private sealed class FakeCurrentUser(bool isAdmin) : ICurrentUser {
        public Guid? UserId(HttpContext ctx) => null;
        public Task<string?> RoleAsync(HttpContext ctx, CancellationToken ct) => Task.FromResult<string?>(isAdmin ? "admin" : "viewer");
        public Task<bool> IsAtLeastAsync(HttpContext ctx, UserRole role, CancellationToken ct) => Task.FromResult(isAdmin);
    }

    private static IdentityApiClient FakeIdentity(Guid adminUserId) {
        var json = JsonSerializer.Serialize(new[] {
            new { userId = adminUserId, discordId = (string?)null, username = "admin", role = "admin" },
        });
        var http = new HttpClient(new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(HttpStatusCode.OK, json))) {
            BaseAddress = new Uri("http://identity.test"),
        };
        return new IdentityApiClient(http);
    }

    private static AdminEndpoints Endpoints(NpgsqlDataSource src, bool isAdmin, Guid adminUserId) =>
        new(new AdminDataService(src, FakeIdentity(adminUserId)), new FakeCurrentUser(isAdmin));

    private static DefaultHttpContext Ctx() {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    private static async Task<JsonDocument> BodyAsync(DefaultHttpContext ctx) {
        ctx.Response.Body.Position = 0;
        using var reader = new StreamReader(ctx.Response.Body);
        return JsonDocument.Parse(await reader.ReadToEndAsync());
    }

    [SkippableFact]
    public async Task Users_SumsStorageAcrossAllTables_AndCountsMissions() {
        TestDbUrl.SkipIfNotConfigured("admin");

        await using var src = NpgsqlDataSource.Create(TestDbUrl.Value!);
        await CreateSchemaAsync(src);
        try {
            var userId = Guid.NewGuid();
            await Exec(src, $"INSERT INTO users (user_id, username, avatar, created_at) VALUES ('{userId}', 'tester', '', to_timestamp(0))");
            await Exec(src, $"INSERT INTO el_mission (user_id, player_id, mission_id, start_timestamp, complete_payload) VALUES ('{userId}', 'EI_A', 'm1', 1, '\\x010203')");
            await Exec(src, $"INSERT INTO el_mission (user_id, player_id, mission_id, start_timestamp, complete_payload) VALUES ('{userId}', 'EI_A', 'm2', 2, '\\x010203')");
            await Exec(src, $"INSERT INTO el_settings (user_id, key, value) VALUES ('{userId}', 'k', 'v')");

            var endpoints = Endpoints(src, isAdmin: true, adminUserId: userId);
            var ctx = Ctx();
            await endpoints.Users(ctx);
            using var doc = await BodyAsync(ctx);

            var row = doc.RootElement.EnumerateArray().Single(u => u.GetProperty("userId").GetGuid() == userId);
            Assert.Equal(2, row.GetProperty("missionCount").GetInt64());
            Assert.True(row.GetProperty("storageBytes").GetInt64() > 0);
        } finally {
            await DropSchemaAsync(src);
        }
    }

    [SkippableFact]
    public async Task DeleteUser_ByUserId_CascadesAndSucceeds() {
        TestDbUrl.SkipIfNotConfigured("admin");

        await using var src = NpgsqlDataSource.Create(TestDbUrl.Value!);
        await CreateSchemaAsync(src);
        try {
            var adminId = Guid.NewGuid();
            var targetId = Guid.NewGuid();
            await Exec(src, $"INSERT INTO users (user_id, username, avatar, created_at) VALUES ('{adminId}', 'admin', '', to_timestamp(0))");
            await Exec(src, $"INSERT INTO users (user_id, username, avatar, created_at) VALUES ('{targetId}', 'target', '', to_timestamp(0))");
            await Exec(src, $"INSERT INTO el_mission (user_id, player_id, mission_id, start_timestamp, complete_payload) VALUES ('{targetId}', 'EI_B', 'm1', 1, '\\x01')");

            var endpoints = Endpoints(src, isAdmin: true, adminUserId: adminId);
            var ctx = Ctx();
            await endpoints.DeleteUser(ctx, targetId.ToString());
            using var doc = await BodyAsync(ctx);
            Assert.True(doc.RootElement.GetProperty("deleted").GetBoolean());

            await using var cmd = src.CreateCommand($"SELECT COUNT(*) FROM el_mission WHERE user_id = '{targetId}'");
            Assert.Equal(0L, (long)(await cmd.ExecuteScalarAsync())!);
        } finally {
            await DropSchemaAsync(src);
        }
    }

    [SkippableFact]
    public async Task Users_NonAdmin_Returns403() {
        TestDbUrl.SkipIfNotConfigured("admin");

        await using var src = NpgsqlDataSource.Create(TestDbUrl.Value!);
        await CreateSchemaAsync(src);
        try {
            var endpoints = Endpoints(src, isAdmin: false, adminUserId: Guid.NewGuid());
            var ctx = Ctx();
            await endpoints.Users(ctx);
            Assert.Equal(StatusCodes.Status403Forbidden, ctx.Response.StatusCode);
        } finally {
            await DropSchemaAsync(src);
        }
    }

    private static async Task CreateSchemaAsync(NpgsqlDataSource src) {
        await Exec(src, $"DROP SCHEMA IF EXISTS {Schema} CASCADE; CREATE SCHEMA {Schema}; SET search_path TO {Schema};");
        await ApplyMigrationAsync(src, "1_initial_schema.up.sql");
        await ApplyMigrationAsync(src, "2_add_user_profile.up.sql");
        await ApplyMigrationAsync(src, "3_add_encryption_key.up.sql");
        await ApplyMigrationAsync(src, "4_eggledger_storage.up.sql");
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
