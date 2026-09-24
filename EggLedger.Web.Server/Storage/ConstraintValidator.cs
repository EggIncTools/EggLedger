using Npgsql;

namespace EggLedger.Web.Server.Storage;

public sealed class ConstraintValidator(NpgsqlDataSource source, TimeProvider time, ILogger<ConstraintValidator> logger) {
    private const string PendingSql = """
        SELECT conrelid::regclass::text, conname
        FROM pg_constraint
        WHERE contype = 'f' AND NOT convalidated
        ORDER BY pg_relation_size(conrelid), conname
        """;

    public async Task RunAsync(CancellationToken ct) {
        try {
            var pending = await PendingAsync(ct);
            if (pending.Count == 0) {
                return;
            }
            logger.LogInformation("constraints: {Count} foreign key(s) not validated, validating in background", pending.Count);
            foreach (var (table, name) in pending) {
                await ValidateAsync(table, name, ct);
            }
        } catch (OperationCanceledException ex) {
            logger.LogDebug(ex, "constraints: background validation cancelled");
        } catch (Exception ex) {
            logger.LogWarning(ex, "constraints: background validation stopped");
        }
    }

    public async Task<IReadOnlyList<(string Table, string Name)>> PendingAsync(CancellationToken ct) {
        List<(string, string)> rows = [];
        await using var conn = await source.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(PendingSql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) {
            rows.Add((reader.GetString(0), reader.GetString(1)));
        }
        return rows;
    }

    private async Task ValidateAsync(string table, string name, CancellationToken ct) {
        var started = time.GetTimestamp();
        try {
            await using var conn = await source.OpenConnectionAsync(ct);
            await using var cmd = new NpgsqlCommand($"ALTER TABLE {Quote(table)} VALIDATE CONSTRAINT {Quote(name)}", conn);
            cmd.CommandTimeout = 0;
            await cmd.ExecuteNonQueryAsync(ct);
            logger.LogInformation("constraints: validated {Constraint} on {Table} in {Elapsed}",
                name, table, time.GetElapsedTime(started));
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            logger.LogWarning(ex, "constraints: failed to validate {Constraint} on {Table}, leaving it NOT VALID", name, table);
        }
    }

    private static string Quote(string identifier) =>
        identifier.Contains('"', StringComparison.Ordinal)
            ? throw new ArgumentException($"illegal identifier {identifier}", nameof(identifier))
            : string.Join('.', identifier.Split('.').Select(part => "\"" + part + "\""));
}
