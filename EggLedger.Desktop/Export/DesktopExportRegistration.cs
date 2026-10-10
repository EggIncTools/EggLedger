using EggLedger.Web.Platform;
using EggLedger.Web.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EggLedger.Desktop.Export;

public static class DesktopExportRegistration {
    public static IServiceCollection AddDesktopExportSink(this IServiceCollection services) {
        services.RemoveAll<IDownloadService>();
        services.RemoveAll<DownloadService>();
        services.AddScoped<IDownloadService>(sp => new DesktopDownloadService(sp.GetRequiredService<IPlatformCapabilities>()));
        return services;
    }
}
