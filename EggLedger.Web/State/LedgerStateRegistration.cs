using Microsoft.Extensions.DependencyInjection;

namespace EggLedger.Web.State;

public static class LedgerStateRegistration {
    public static IServiceCollection AddLedgerState(this IServiceCollection services) {
        services.AddScoped<LedgerShellState>();
        services.AddScoped<FilterState>();
        services.AddScoped<ShipsViewState>();
        services.AddScoped<DropsViewState>();
        services.AddScoped<ReportsViewState>();
        return services;
    }
}
