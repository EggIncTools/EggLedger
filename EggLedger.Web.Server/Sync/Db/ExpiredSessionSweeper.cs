using Npgsql;

namespace EggLedger.Web.Server.Sync.Db;

public sealed class ExpiredSessionSweeper(NpgsqlDataSource source, TimeSpan interval, ILogger<ExpiredSessionSweeper> logger) {
    public async Task RunAsync(CancellationToken ct) {
        using var timer = new PeriodicTimer(interval);
        try {
            while (await timer.WaitForNextTickAsync(ct))
                await SweepAsync(ct);
        } catch (OperationCanceledException ex) {
            logger.LogDebug(ex, "sessions: expired-session sweeper stopped on shutdown");
        }
    }

    public async Task SweepAsync(CancellationToken ct) {
        await using var conn = await source.OpenConnectionAsync(ct);
        await using (var cmd = new NpgsqlCommand("DELETE FROM sessions WHERE expires_at < now()", conn))
            await cmd.ExecuteNonQueryAsync(ct);
        await using (var cmd = new NpgsqlCommand("DELETE FROM pending_auth WHERE expires_at < now()", conn))
            await cmd.ExecuteNonQueryAsync(ct);
    }
}
