using EggLedger.Domain.Api;
using EggLedger.Domain.MissionPacking;
using EggLedger.Domain.MissionQuery;
using EggLedger.Domain.Reports;
using EggLedger.Web.Data;
using EggLedger.Web.Platform;
using EggLedger.Web.Services;
using EggLedger.Web.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EggLedger.Web;

public static class WebServiceRegistration {
    public static IServiceCollection AddEggLedgerWeb(this IServiceCollection services, Uri httpBaseAddress) {
        services.AddScoped(_ => new HttpClient { BaseAddress = httpBaseAddress });


        services.AddScoped<IndexedDbSettings>();
        services.AddScoped<IndexedDbAccountStore>();
        services.AddScoped<IndexedDbReportStore>();
        services.AddScoped<IndexedDbMissionStore>(sp => new IndexedDbMissionStore(
            sp.GetRequiredService<IIndexedDb>(),
            sp.GetRequiredService<IApiPayloadDecoder>(),
            accounts: sp.GetRequiredService<IndexedDbAccountStore>(),
            logger: sp.GetService<ILogger<IndexedDbMissionStore>>()));
        services.AddScoped<IMissionStore>(sp => sp.GetRequiredService<IndexedDbMissionStore>());
        services.AddScoped<IReportSourceCache>(sp => {
            var cache = new ReportSourceCache(IndexedDbReportSource.Loader(sp.GetRequiredService<IIndexedDb>()));
            cache.AttachHub(sp.GetRequiredService<LedgerDataHub>());
            return cache;
        });
        services.AddScoped<IndexedDbReportRunner>();
        services.AddScoped<IReportRunner>(sp => sp.GetRequiredService<IndexedDbReportRunner>());

        services.AddSingleton<IArtifactQuality>(_ => EiafxQualityAdapter.Instance);
        services.AddScoped<IMissionCompiler>(_ => new MissionPacker(EiafxMissionConfigSource.Instance));
        services.AddScoped<MissionQueryHandlers>();

        services.AddSingleton<EggLedger.Web.Missions.MissionConfigProvider>();


        services.AddScoped(sp => new ApiClient(sp.GetRequiredService<HttpClient>(), apiPrefix: "/egg-api"));

        services.AddScoped<IApiPayloadDecoder>(sp => new LocalApiPayloadDecoder(sp.GetRequiredService<ApiClient>()));

        services.AddScoped<FetchService>();
        services.AddScoped<FetchOrchestrator>();
        services.AddScoped<AddAccountService>();


        services.AddScoped<DownloadService>();
        services.AddScoped<IDownloadService>(sp => sp.GetRequiredService<DownloadService>());
        services.AddScoped<IAutoExporter, BrowserAutoExporter>();

        services.AddSingleton<IWeightData>(_ => EiafxWeightData.Instance);



        services.AddSingleton(_ => new MennoService(new HttpClient()));

        services.AddSingleton(sp => new GameEventsService(
            new HttpClient { Timeout = GameEventsService.RequestTimeout },
            Environment.GetEnvironmentVariable(GameEventsService.BaseUrlVariable),
            Environment.GetEnvironmentVariable(GameEventsService.ApiKeyVariable),
            store: null,
            logger: sp.GetService<ILogger<GameEventsService>>()));


        services.AddScoped<INavigation, BlazorNavigation>();
        services.AddScoped<IBlobCipher, LocalBlobCipher>();
        services.AddScoped<CloudSyncService>();
        services.AddScoped<CloudAutoSyncCoordinator>();
        services.AddScoped<AdminState>();
        services.AddScoped<EggLedger.Web.Settings.CloudSessionStore>();
        services.AddScoped<EggLedger.Web.Settings.SettingsWriteCoordinator>();


        services.AddScoped<ActiveAccount>();
        services.AddScoped<ScreenshotSafetyState>();
        services.AddScoped<AppStateService>();
        services.AddScoped(sp => {
            var hub = new LedgerDataHub(
                sp.GetRequiredService<MissionQueryHandlers>(),
                sp.GetRequiredService<IndexedDbMissionStore>());
            hub.AttachFetch(sp.GetRequiredService<FetchOrchestrator>());
            return hub;
        });
        services.AddScoped<AccountLoader>();
        services.AddLedgerState();
        services.AddScoped<IPlatformCapabilities, BrowserPlatformCapabilities>();


        services.AddScoped<BrowserStorageManagement>();
        services.AddScoped<IStorageManagement>(sp => sp.GetRequiredService<BrowserStorageManagement>());
        services.AddScoped<IExportManagement>(sp => sp.GetRequiredService<BrowserStorageManagement>());


        services.AddScoped<IUpdateStatusProvider, NoOpUpdateStatusProvider>();

        return services;
    }
}
