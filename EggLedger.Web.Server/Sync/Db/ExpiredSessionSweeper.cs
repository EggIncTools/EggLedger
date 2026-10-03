using EggIdentity.Hosting;
using Npgsql;

namespace EggLedger.Web.Server.Sync.Db;

public sealed class ExpiredSessionSweeper(IServiceScopeFactory scopes, TimeProvider time, TimeSpan interval, ILogger<ExpiredSessionSweeper> logger)
    : PeriodicScopedService(scopes, time, logger) {
    protected override TimeSpan Interval => interval;

    protected override bool RunAtStart => false;

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct) {
        await using var conn = await services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        await using (var cmd = new NpgsqlCommand("DELETE FROM sessions WHERE expires_at < now()", conn))
            await cmd.ExecuteNonQueryAsync(ct);
        await using (var cmd = new NpgsqlCommand("DELETE FROM pending_auth WHERE expires_at < now()", conn))
            await cmd.ExecuteNonQueryAsync(ct);
    }
}
