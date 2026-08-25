using EggLedger.Web.Server.SubProd;
using Npgsql;

namespace EggLedger.SubProd;

public static class Sanitize {
    public static async Task<int> RunAsync() {
        var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
        if (string.IsNullOrEmpty(databaseUrl)) {
            Console.Error.WriteLine("eggledger.subprod sanitize: DATABASE_URL not set");
            return 1;
        }

        try {
            SubProdBootGuard.EnsureSubProdDatabase(databaseUrl);
        } catch (InvalidOperationException ex) {
            Console.Error.WriteLine($"eggledger.subprod sanitize: {ex.Message}");
            return 1;
        }

        await using var source = NpgsqlDataSource.Create(databaseUrl);

        await using (var cmd = source.CreateCommand("TRUNCATE data_protection_keys")) {
            await cmd.ExecuteNonQueryAsync();
        }
        await using (var cmd = source.CreateCommand("TRUNCATE sessions")) {
            await cmd.ExecuteNonQueryAsync();
        }
        await using (var cmd = source.CreateCommand("TRUNCATE pending_auth")) {
            await cmd.ExecuteNonQueryAsync();
        }
        await using (var cmd = source.CreateCommand("UPDATE users SET encryption_key = ''")) {
            await cmd.ExecuteNonQueryAsync();
        }
        await using (var cmd = source.CreateCommand("TRUNCATE deploy_state")) {
            await cmd.ExecuteNonQueryAsync();
        }
        await using (var cmd = source.CreateCommand("TRUNCATE bot_channel_config")) {
            await cmd.ExecuteNonQueryAsync();
        }
        await using (var cmd = source.CreateCommand(
            "CREATE TABLE IF NOT EXISTS subprod_stamp (sanitized_at TIMESTAMPTZ NOT NULL)")) {
            await cmd.ExecuteNonQueryAsync();
        }
        await using (var cmd = source.CreateCommand("INSERT INTO subprod_stamp (sanitized_at) VALUES (now())")) {
            await cmd.ExecuteNonQueryAsync();
        }

        Console.WriteLine("eggledger.subprod sanitize: done");
        return 0;
    }
}
