using EggLedger.Web.Server.SubProd;
using Npgsql;

namespace EggLedger.SubProd;

public static class Verify {
    private static readonly (string Description, string Sql)[] ZeroCountAssertions = [
        ("data_protection_keys is empty", "SELECT count(*) FROM data_protection_keys"),
        ("sessions is empty", "SELECT count(*) FROM sessions"),
        ("pending_auth is empty", "SELECT count(*) FROM pending_auth"),
        ("no user has a non-empty encryption_key", "SELECT count(*) FROM users WHERE encryption_key <> ''"),
        ("deploy_state is empty", "SELECT count(*) FROM deploy_state"),
        ("bot_channel_config is empty", "SELECT count(*) FROM bot_channel_config"),
    ];

    public static async Task<int> RunAsync() {
        var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
        if (string.IsNullOrEmpty(databaseUrl)) {
            Console.Error.WriteLine("eggledger.subprod verify: DATABASE_URL not set");
            return 1;
        }

        try {
            SubProdBootGuard.EnsureSubProdDatabase(databaseUrl);
        } catch (InvalidOperationException ex) {
            Console.Error.WriteLine($"eggledger.subprod verify: {ex.Message}");
            return 1;
        }

        await using var source = NpgsqlDataSource.Create(databaseUrl);
        var ok = await RunAsync(source);
        return ok ? 0 : 1;
    }

    public static async Task<bool> RunAsync(NpgsqlDataSource source) {
        var allPassed = true;

        foreach (var (description, sql) in ZeroCountAssertions) {
            await using var cmd = source.CreateCommand(sql);
            var count = (long)(await cmd.ExecuteScalarAsync() ?? 0L);
            var passed = count == 0;
            allPassed &= passed;
            Console.WriteLine($"{(passed ? "PASS" : "FAIL")}: {description} (count={count})");
        }

        await using (var cmd = source.CreateCommand("SELECT current_database()")) {
            var current = (string)(await cmd.ExecuteScalarAsync() ?? "");
            var passed = string.Equals(current, SubProdFence.SubProdDatabaseName, StringComparison.Ordinal);
            allPassed &= passed;
            Console.WriteLine($"{(passed ? "PASS" : "FAIL")}: current_database() is '{SubProdFence.SubProdDatabaseName}' (got '{current}')");
        }

        return allPassed;
    }
}
