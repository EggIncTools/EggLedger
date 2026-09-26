using System.Net;
using System.Security.Cryptography;
using System.Text;
using EggIdentity.Client;
using EggLedger.Domain.Crypto;
using EggLedger.Web.Server.Sync.Db;
using EggLedger.Web.Server.Tests.Sync.Auth;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace EggLedger.Web.Server.Tests.Sync.Db;

public sealed class AccountMergeRemapperTests {
    private const string Schema = "eltest_mergeremap";

    private static string NewKey() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    [Fact]
    public void Rewrap_reencrypts_under_the_target_key() {
        var (from, to) = (NewKey(), NewKey());
        var ciphertext = BlobCrypto.Encrypt(from, Encoding.UTF8.GetBytes("payload"));

        var rewrapped = AccountMergeRemapper.Rewrap(from, to, ciphertext);

        Assert.NotNull(rewrapped);
        Assert.Equal("payload", Encoding.UTF8.GetString(BlobCrypto.Decrypt(to, rewrapped)));
        Assert.Throws<AuthenticationTagMismatchException>(() => BlobCrypto.Decrypt(from, rewrapped));
    }

    [Fact]
    public void Rewrap_returns_null_when_source_key_is_wrong() {
        var ciphertext = BlobCrypto.Encrypt(NewKey(), Encoding.UTF8.GetBytes("payload"));

        Assert.Null(AccountMergeRemapper.Rewrap(NewKey(), NewKey(), ciphertext));
        Assert.Null(AccountMergeRemapper.Rewrap(NewKey(), NewKey(), "not base64!"));
    }

    [Fact]
    public void CollisionSql_matches_on_every_unique_key_column() {
        var sql = AccountMergeRemapper.CollisionSql(new("el_mission", ["player_id", "mission_id"]));

        Assert.Equal(
            "DELETE FROM \"el_mission\" m USING \"el_mission\" k WHERE m.user_id = $1 AND k.user_id = $2" +
            " AND m.player_id = k.player_id AND m.mission_id = k.mission_id",
            sql);
    }

    [Fact]
    public void Tables_cover_every_stored_data_table() {
        var remapped = AccountMergeRemapper.Tables.Select(t => t.Name).ToHashSet();

        Assert.All(Server.Sync.UserDataDeletion.StoredDataTables, t => Assert.Contains(t, remapped));
        Assert.Contains("blobs", remapped);
        Assert.Contains("sessions", remapped);
    }

    [SkippableFact]
    public async Task Sweep_moves_rows_to_kept_user_and_kept_wins_collisions() {
        TestDbUrl.SkipIfNotConfigured("merge remap");
        await using var setupSrc = NpgsqlDataSource.Create(TestDbUrl.Value!);
        await CreateSchemaAsync(setupSrc);
        await using var src = ScopedSource();
        try {
            var (merged, kept) = (Guid.NewGuid(), Guid.NewGuid());
            await SeedUserAsync(src, merged, "");
            await SeedUserAsync(src, kept, "");
            await Exec(src, $"""
                INSERT INTO el_settings (user_id, key, value) VALUES
                    ('{merged}', 'theme', 'merged'), ('{merged}', 'only_merged', 'm'), ('{kept}', 'theme', 'kept');
                INSERT INTO identity_merge_watermark (id, merged_at) VALUES (TRUE, '2026-01-01T00:00:00Z');
                """);
            var mergedAt = DateTimeOffset.Parse("2026-09-26T12:00:00Z");
            var remapper = RemapperFor(src, new EphemeralDataProtectionProvider(), Feed((merged, kept, mergedAt)));

            Assert.Equal(1, await remapper.SweepAsync(CancellationToken.None));

            Assert.Equal(["only_merged=m", "theme=kept"], await SettingsAsync(src, kept));
            Assert.Empty(await SettingsAsync(src, merged));
            Assert.Equal(0L, await ScalarAsync<long>(src, $"SELECT COUNT(*) FROM users WHERE user_id = '{merged}'"));
            Assert.Equal(mergedAt, await ScalarAsync<DateTimeOffset>(src, "SELECT merged_at FROM identity_merge_watermark"));
        } finally {
            await DropSchemaAsync(setupSrc);
        }
    }

    [SkippableFact]
    public async Task Sweep_renames_local_user_when_kept_user_has_no_row() {
        TestDbUrl.SkipIfNotConfigured("merge remap");
        await using var setupSrc = NpgsqlDataSource.Create(TestDbUrl.Value!);
        await CreateSchemaAsync(setupSrc);
        await using var src = ScopedSource();
        try {
            var (merged, kept) = (Guid.NewGuid(), Guid.NewGuid());
            await SeedUserAsync(src, merged, "stored-key");
            await Exec(src, $"INSERT INTO el_settings (user_id, key, value) VALUES ('{merged}', 'theme', 'm');");
            var remapper = RemapperFor(src, new EphemeralDataProtectionProvider(), Feed((merged, kept, DateTimeOffset.UtcNow)));

            await remapper.SweepAsync(CancellationToken.None);

            Assert.Equal("stored-key", await ScalarAsync<string>(src, $"SELECT encryption_key FROM users WHERE user_id = '{kept}'"));
            Assert.Equal(["theme=m"], await SettingsAsync(src, kept));
        } finally {
            await DropSchemaAsync(setupSrc);
        }
    }

    [SkippableFact]
    public async Task Sweep_reencrypts_merged_blobs_under_kept_key_and_is_idempotent() {
        TestDbUrl.SkipIfNotConfigured("merge remap");
        await using var setupSrc = NpgsqlDataSource.Create(TestDbUrl.Value!);
        await CreateSchemaAsync(setupSrc);
        await using var src = ScopedSource();
        try {
            var dp = new EphemeralDataProtectionProvider();
            var protector = dp.CreateProtector("EggLedger.EncryptionKey");
            var (merged, kept) = (Guid.NewGuid(), Guid.NewGuid());
            var (mergedKey, keptKey) = (NewKey(), NewKey());
            await SeedUserAsync(src, merged, protector.Protect(mergedKey));
            await SeedUserAsync(src, kept, protector.Protect(keptKey));
            await SeedBlobAsync(src, merged, "accounts", BlobCrypto.Encrypt(mergedKey, "merged-accounts"u8.ToArray()));
            await SeedBlobAsync(src, merged, "settings", BlobCrypto.Encrypt(mergedKey, "merged-settings"u8.ToArray()));
            await SeedBlobAsync(src, kept, "settings", BlobCrypto.Encrypt(keptKey, "kept-settings"u8.ToArray()));
            var remapper = RemapperFor(src, dp, Feed((merged, kept, DateTimeOffset.UtcNow)));

            await remapper.SweepAsync(CancellationToken.None);
            await remapper.SweepAsync(CancellationToken.None);

            Assert.Equal("merged-accounts", await DecryptBlobAsync(src, kept, "accounts", keptKey));
            Assert.Equal("kept-settings", await DecryptBlobAsync(src, kept, "settings", keptKey));
            Assert.Equal(0L, await ScalarAsync<long>(src, $"SELECT COUNT(*) FROM blobs WHERE user_id = '{merged}'"));
            Assert.Equal(2L, await ScalarAsync<long>(src, $"SELECT COUNT(*) FROM blobs WHERE user_id = '{kept}'"));
        } finally {
            await DropSchemaAsync(setupSrc);
        }
    }

    private static AccountMergeRemapper RemapperFor(NpgsqlDataSource src, IDataProtectionProvider dp, string feedJson) =>
        new(src,
            new IdentityApiClient(new HttpClient(new StubHttpMessageHandler(_ =>
                StubHttpMessageHandler.Json(HttpStatusCode.OK, feedJson))) { BaseAddress = new Uri("http://localhost:8090") }),
            dp,
            NullLogger<AccountMergeRemapper>.Instance);

    private static string Feed(params (Guid Merged, Guid Kept, DateTimeOffset At)[] merges) =>
        "[" + string.Join(",", merges.Select(m =>
            $$"""{"mergedUserId":"{{m.Merged}}","keptUserId":"{{m.Kept}}","mergedAt":"{{m.At:O}}"}""")) + "]";

    private static NpgsqlDataSource ScopedSource() =>
        NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(TestDbUrl.Value!) { SearchPath = Schema }.ConnectionString);

    private static async Task SeedUserAsync(NpgsqlDataSource src, Guid userId, string encryptionKey) {
        await using var cmd = src.CreateCommand("INSERT INTO users (user_id, created_at, encryption_key) VALUES ($1, now(), $2)");
        cmd.Parameters.AddWithValue(userId);
        cmd.Parameters.AddWithValue(encryptionKey);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task SeedBlobAsync(NpgsqlDataSource src, Guid userId, string name, string ciphertext) {
        await using var cmd = src.CreateCommand("INSERT INTO blobs (user_id, name, ciphertext, updated_at) VALUES ($1, $2, $3, now())");
        cmd.Parameters.AddWithValue(userId);
        cmd.Parameters.AddWithValue(name);
        cmd.Parameters.AddWithValue(ciphertext);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<string> DecryptBlobAsync(NpgsqlDataSource src, Guid userId, string name, string hexKey) {
        var ciphertext = await ScalarAsync<string>(src, $"SELECT ciphertext FROM blobs WHERE user_id = '{userId}' AND name = '{name}'");
        return Encoding.UTF8.GetString(BlobCrypto.Decrypt(hexKey, ciphertext));
    }

    private static async Task<List<string>> SettingsAsync(NpgsqlDataSource src, Guid userId) {
        List<string> rows = [];
        await using var cmd = src.CreateCommand($"SELECT key || '=' || value FROM el_settings WHERE user_id = '{userId}' ORDER BY key");
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add(reader.GetString(0));
        return rows;
    }

    private static async Task<T> ScalarAsync<T>(NpgsqlDataSource src, string sql) {
        await using var cmd = src.CreateCommand(sql);
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        return reader.GetFieldValue<T>(0);
    }

    private static async Task CreateSchemaAsync(NpgsqlDataSource src) {
        await Exec(src, $"DROP SCHEMA IF EXISTS {Schema} CASCADE; CREATE SCHEMA {Schema};");
        var migrations = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Migrations"), "*.up.sql")
            .OrderBy(f => int.Parse(Path.GetFileName(f).Split('_')[0]));
        foreach (var file in migrations) {
            await Exec(src, $"SET search_path TO {Schema}; {await File.ReadAllTextAsync(file)}");
        }
    }

    private static async Task DropSchemaAsync(NpgsqlDataSource src) =>
        await Exec(src, $"DROP SCHEMA IF EXISTS {Schema} CASCADE;");

    private static async Task Exec(NpgsqlDataSource src, string sql) {
        await using var cmd = src.CreateCommand(sql);
        await cmd.ExecuteNonQueryAsync();
    }
}
