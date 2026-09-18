using System.Security.Claims;
using EggLedger.Web.Data;
using EggLedger.Web.Server.Auth;
using EggLedger.Web.Server.Storage;
using EggLedger.Web.Server.Tests.Sync.Auth;
using Microsoft.AspNetCore.Components.Authorization;
using Npgsql;

namespace EggLedger.Web.Server.Tests;

public sealed class PostgresIsolationTests {
    private sealed class FakeAuth(Guid? userId) : AuthenticationStateProvider {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() {
            var identity = userId is null
                ? new ClaimsIdentity()
                : new ClaimsIdentity([new Claim(AuthScheme.UserIdClaim, userId.Value.ToString())], "test");
            return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity)));
        }
    }

    private static PostgresIndexedDb StoreFor(NpgsqlDataSource src, Guid? userId) =>
        new(src, new CurrentUser(new FakeAuth(userId)));

    private static MissionRow Mission(string player, string id) => new() {
        PlayerId = player,
        MissionId = id,
        StartTimestamp = 1,
        CompletePayload = [1, 2, 3],
        MissionType = 0,
    };

    [SkippableFact]
    public async Task UserA_NeverReadsUserB_Rows() {
        TestDbUrl.SkipIfNotConfigured("isolation");

        await using var src = NpgsqlDataSource.Create(TestDbUrl.Value!);
        await CreateSchemaAsync(src);
        try {
            var a = StoreFor(src, Guid.NewGuid());
            var b = StoreFor(src, Guid.NewGuid());

            await a.PutAsync("mission", Mission("EI_A", "m1"));
            await a.PutAsync("mission", Mission("EI_A", "m2"));
            await b.PutAsync("mission", Mission("EI_B", "m9"));

            var aAll = await a.GetAllAsync<MissionRow>("mission");
            var bAll = await b.GetAllAsync<MissionRow>("mission");

            Assert.Equal(2, aAll.Length);
            Assert.All(aAll, m => Assert.Equal("EI_A", m.PlayerId));
            Assert.Single(bAll);
            Assert.Equal("EI_B", bAll[0].PlayerId);

            var leaked = await b.GetAsync<MissionRow>("mission", new object[] { "EI_A", "m1" });
            Assert.Null(leaked);

            Assert.Equal(2, await a.CountAsync("mission"));
            Assert.Equal(1, await b.CountAsync("mission"));

            await b.ClearAsync("mission");
            Assert.Equal(2, (await a.GetAllAsync<MissionRow>("mission")).Length);
            Assert.Empty(await b.GetAllAsync<MissionRow>("mission"));
        } finally {
            await DropSchemaAsync(src);
        }
    }

    [SkippableFact]
    public async Task Unauthenticated_StorageThrows() {
        TestDbUrl.SkipIfNotConfigured("isolation");

        await using var src = NpgsqlDataSource.Create(TestDbUrl.Value!);
        await CreateSchemaAsync(src);
        try {
            var anon = StoreFor(src, null);

            Assert.Empty(await anon.GetAllAsync<MissionRow>("mission"));
            await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await anon.PutAsync("mission", Mission("EI_X", "x1")));
        } finally {
            await DropSchemaAsync(src);
        }
    }

    private const string Schema = "eltest_iso";

    private static async Task CreateSchemaAsync(NpgsqlDataSource src) {
        await Exec(src, $"DROP SCHEMA IF EXISTS {Schema} CASCADE; CREATE SCHEMA {Schema}; SET search_path TO {Schema};");

        await ApplyMigrationAsync(src, "1_initial_schema.up.sql");
        await ApplyMigrationAsync(src, "4_eggledger_storage.up.sql");
        await ApplyMigrationAsync(src, "8_identities.up.sql");
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
