using Npgsql;

namespace EggLedger.Web.Server.Storage;

public static class MigrationLedger {
    public const string Table = "eggledger_migrations";

    private const string LegacyTable = "eggidentity_migrations";

    public static async Task AdoptLegacyAsync(NpgsqlConnection conn, CancellationToken ct = default) {
        await using var cmd = new NpgsqlCommand($"""
            CREATE TABLE IF NOT EXISTS {Table} (version INTEGER PRIMARY KEY);
            DO $$ BEGIN
                IF to_regclass('{LegacyTable}') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM {Table}) THEN
                    EXECUTE 'INSERT INTO {Table} (version) SELECT version FROM {LegacyTable} ON CONFLICT DO NOTHING';
                END IF;
            END $$;
            """, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
