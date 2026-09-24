using EggLedger.Web.Data;
using EggLedger.Web.Platform;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace EggLedger.Desktop.Update;

public static class UpdateRegistration {
    public static IServiceCollection AddDesktopUpdater(
        this IServiceCollection services, Func<string> runningVersion, Func<Task>? exitAction = null) {
        var exit = exitAction ?? (() => {
            Environment.Exit(0);
            return Task.CompletedTask;
        });

        services.AddSingleton(sp => new GithubReleaseClient(
            sp.GetService<HttpClient>() ?? new HttpClient(),
            sp.GetService<ILogger<GithubReleaseClient>>()));

        services.RemoveAll<IUpdateStatusProvider>();
        services.AddSingleton<IUpdateStatusProvider>(sp => new UpdateService(
            sp.GetRequiredService<GithubReleaseClient>(),
            runningVersion,
            exitAction: exit,

            settings: new IndexedDbSettings(sp.GetRequiredService<IIndexedDb>()),
            time: sp.GetRequiredService<TimeProvider>(),
            logger: sp.GetService<ILogger<UpdateService>>()));

        return services;
    }
}
