using EggLedger.Web.Services;

namespace EggLedger.Web.Server.Ships;

public sealed class EventIconWarmupHostedService(GameEventsService events, EventIconCache icons, ILogger<EventIconWarmupHostedService> logger) : IHostedService {
    public Task StartAsync(CancellationToken cancellationToken) {
        if (!events.IsConfigured || !icons.IsConfigured) {
            return Task.CompletedTask;
        }

        _ = Task.Run(() => WarmAsync(cancellationToken), CancellationToken.None);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task WarmAsync(CancellationToken cancellationToken) {
        try {
            await events.EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            var keys = events.Snapshot()
                .Select(e => (e.Type, e.Ultra))
                .Distinct()
                .ToList();
            await icons.WarmAsync(keys, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("event icons: warmed {Count} icons for {Types} event types", icons.Count, keys.Count);
        } catch (Exception ex) {
            logger.LogWarning(ex, "event icon warmup failed, continuing without cached icons");
        }
    }
}
