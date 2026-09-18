using EggLedger.Web.Server.Tests.Sync.Auth;
using Npgsql;

namespace EggLedger.Web.Server.Tests.Migrations;

public sealed class IdentitiesMigrationTests {
    private const string Schema = "eltest_identities";

    [SkippableFact]
    public async Task Migration7_BackfillsUserIdAndRepointsPrimaryKey() {
        TestDbUrl.SkipIfNotConfigured("migration");

        await using var src = NpgsqlDataSource.Create(TestDbUrl.Value!);
        await Exec(src, $"DROP SCHEMA IF EXISTS {Schema} CASCADE; CREATE SCHEMA {Schema}; SET search_path TO {Schema};");
        try {
            await ApplyMigrationAsync(src, "1_initial_schema.up.sql");
            await ApplyMigrationAsync(src, "4_eggledger_storage.up.sql");

            await Exec(src, $"""
                SET search_path TO {Schema};
                INSERT INTO users (discord_id, created_at) VALUES ('USER_A', 1000), ('USER_B', 2000);
                INSERT INTO el_mission (discord_id, player_id, mission_id, start_timestamp, complete_payload, mission_type)
                VALUES
                    ('USER_A', 'EI_A', 'm1', 1, '\x010203', 0),
                    ('USER_A', 'EI_A', 'm2', 2, '\x010203', 0),
                    ('USER_B', 'EI_B', 'm9', 3, '\x010203', 0);
                """);

            await ApplyMigrationAsync(src, "8_identities.up.sql");

            await using (var cmd = src.CreateCommand($"SET search_path TO {Schema}; SELECT discord_id, user_id FROM users ORDER BY discord_id;")) {
                await using var reader = await cmd.ExecuteReaderAsync();
                var userIds = new List<Guid>();
                while (await reader.ReadAsync()) {
                    Assert.False(reader.IsDBNull(1));
                    userIds.Add(reader.GetGuid(1));
                }
                Assert.Equal(2, userIds.Count);
                Assert.Equal(userIds.Count, userIds.Distinct().Count());
            }

            await using (var cmd = src.CreateCommand($"""
                SET search_path TO {Schema};
                SELECT u.discord_id, i.provider, i.subject
                FROM identities i
                JOIN users u ON u.user_id = i.user_id
                ORDER BY u.discord_id;
                """)) {
                await using var reader = await cmd.ExecuteReaderAsync();
                var rows = new List<(string DiscordId, string Provider, string Subject)>();
                while (await reader.ReadAsync()) {
                    rows.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
                }
                Assert.Equal(2, rows.Count);
                Assert.All(rows, r => Assert.Equal("discord", r.Provider));
                Assert.All(rows, r => Assert.Equal(r.DiscordId, r.Subject));
            }

            await using (var cmd = src.CreateCommand($"""
                SET search_path TO {Schema};
                SELECT m.player_id, i.subject
                FROM el_mission m
                JOIN identities i ON i.user_id = m.user_id AND i.provider = 'discord'
                ORDER BY m.mission_id;
                """)) {
                await using var reader = await cmd.ExecuteReaderAsync();
                var rows = new List<(string PlayerId, string DiscordId)>();
                while (await reader.ReadAsync()) {
                    rows.Add((reader.GetString(0), reader.GetString(1)));
                }
                Assert.Equal(3, rows.Count);
                Assert.All(rows.Where(r => r.PlayerId == "EI_A"), r => Assert.Equal("USER_A", r.DiscordId));
                Assert.All(rows.Where(r => r.PlayerId == "EI_B"), r => Assert.Equal("USER_B", r.DiscordId));
            }

            var dupeId = Guid.NewGuid();
            await Exec(src, $"SET search_path TO {Schema}; INSERT INTO users (user_id, discord_id, created_at) VALUES ('{dupeId}', 'USER_C', 3000);");
            await Assert.ThrowsAsync<PostgresException>(async () =>
                await Exec(src, $"SET search_path TO {Schema}; INSERT INTO users (user_id, discord_id, created_at) VALUES ('{dupeId}', 'USER_D', 4000);"));

            await using (var cmd = src.CreateCommand($"""
                SET search_path TO {Schema};
                SELECT kcu.column_name
                FROM information_schema.table_constraints tc
                JOIN information_schema.key_column_usage kcu
                    ON tc.constraint_name = kcu.constraint_name AND tc.table_schema = kcu.table_schema
                WHERE tc.table_name = 'users' AND tc.constraint_type = 'PRIMARY KEY' AND tc.table_schema = '{Schema}';
                """)) {
                await using var reader = await cmd.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal("user_id", reader.GetString(0));
                Assert.False(await reader.ReadAsync());
            }
        } finally {
            await Exec(src, $"DROP SCHEMA IF EXISTS {Schema} CASCADE;");
        }
    }

    private static async Task ApplyMigrationAsync(NpgsqlDataSource src, string fileName) {
        var sqlPath = Path.Combine(AppContext.BaseDirectory, "Migrations", fileName);
        var sql = await File.ReadAllTextAsync(sqlPath);
        await Exec(src, $"SET search_path TO {Schema}; {sql}");
    }

    private static async Task Exec(NpgsqlDataSource src, string sql) {
        await using var cmd = src.CreateCommand(sql);
        await cmd.ExecuteNonQueryAsync();
    }
}
