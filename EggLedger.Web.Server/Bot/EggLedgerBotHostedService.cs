using EggIdentity.Bot;

namespace EggLedger.Web.Server.Bot;

public sealed class EggLedgerBotHostedService(BotConfig config, ILogger<EggLedgerBotHostedService> logger) : IHostedService {
    public EggIdentityBot? Bot { get; private set; }

    public async Task StartAsync(CancellationToken cancellationToken) {
        try {
            Bot = await EggIdentityBot.StartAsync(config);
        } catch (Exception ex) {
            logger.LogWarning(ex, "eggidentity: bot start failed, continuing");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken) {
        if (Bot is not null) await Bot.DisposeAsync();
    }
}
