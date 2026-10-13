using EggLedger.Domain.Api;
using EggLedger.Web.Platform;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Photino.NET;

namespace EggLedger.Desktop.Platform;

public static class DesktopPlatformRegistration {
    public static IServiceCollection AddDesktopPlatformCapabilities(
        this IServiceCollection services, PhotinoWindow window)
        => services.AddDesktopPlatformCapabilities(new ProcessRunner(), new PhotinoDesktopWindow(window));

    public static IServiceCollection AddDesktopPlatformCapabilities(
        this IServiceCollection services, IProcessRunner processRunner, IDesktopWindow window) {
        services.AddSingleton(processRunner);
        services.AddSingleton(window);

        services.RemoveAll<IPlatformCapabilities>();
        services.AddSingleton<IPlatformCapabilities>(
            new DesktopPlatformCapabilities(processRunner, window));

        services.RemoveAll<IUserTimeZoneProvider>();
        services.AddSingleton<IUserTimeZoneProvider, DesktopTimeZoneProvider>();

        services.AddEggLedgerApi(ApiClient.DefaultApiPrefix);

        return services;
    }
}
