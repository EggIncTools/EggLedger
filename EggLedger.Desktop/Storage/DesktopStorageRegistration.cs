using EggLedger.Desktop.Export;
using EggLedger.Domain.MissionQuery;
using EggLedger.Domain.Reports;
using EggLedger.Web.Data;
using EggLedger.Web.Platform;
using EggLedger.Web.Services;
using EggLedger.Web.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace EggLedger.Desktop.Storage;

public static class DesktopStorageRegistration {
    public static IServiceCollection AddDesktopSqliteStorage(this IServiceCollection services, string dataRootDir) {
        var internalDir = StoragePaths.ResolveInternalDir(dataRootDir);
        var missionDbPath = Path.Combine(internalDir, "ledger.db");
        var reportDbPath = Path.Combine(internalDir, "reports.db");

        var missionDb = SqliteDatabase.OpenMissionDb(missionDbPath);
        var reportDb = SqliteDatabase.OpenReportDb(reportDbPath);

        AddDesktopSqliteStorage(services, missionDb, reportDb);



        services.AddScoped(sp => new DesktopStorageService(
            dataRootDir, sp.GetRequiredService<IPlatformCapabilities>()));
        services.RemoveAll<IStorageManagement>();
        services.AddScoped<IStorageManagement>(sp => sp.GetRequiredService<DesktopStorageService>());

        services.AddScoped(sp => {
            var accounts = sp.GetRequiredService<IndexedDbAccountStore>();
            return new DesktopExportService(
                dataRootDir,
                accountLookup: async () => (IReadOnlyList<AccountInfo>)await accounts.GetKnownAccountsAsync());
        });
        services.RemoveAll<IExportManagement>();
        services.AddScoped<IExportManagement>(sp => sp.GetRequiredService<DesktopExportService>());

        services.RemoveAll<IAutoExporter>();
        services.AddScoped<IAutoExporter>(sp => new DesktopAutoExporter(
            dataRootDir,
            sp.GetRequiredService<IndexedDbSettings>(),
            sp.GetRequiredService<IMissionStore>(),
            sp.GetRequiredService<MissionQueryHandlers>()));

        services.RemoveAll<MennoService>();
        services.AddSingleton(_ => new MennoService(new HttpClient(), new FileMennoDataStore(internalDir)));

        services.RemoveAll<GameEventsService>();
        services.AddSingleton(sp => new GameEventsService(
            new HttpClient { Timeout = GameEventsService.RequestTimeout },
            Environment.GetEnvironmentVariable(GameEventsService.BaseUrlVariable),
            Environment.GetEnvironmentVariable(GameEventsService.ApiKeyVariable),
            new FileGameEventStore(internalDir),
            sp.GetService<ILogger<GameEventsService>>()));
        return services;
    }

    public static IServiceCollection AddDesktopSqliteStorage(
        this IServiceCollection services, SqliteDatabase missionDb, SqliteDatabase reportDb) {
        services.AddSingleton(missionDb);
        services.AddSingleton(reportDb);

        var indexedDb = new SqliteIndexedDb(missionDb, reportDb);


        services.RemoveAll<IIndexedDb>();
        services.AddSingleton<IIndexedDb>(indexedDb);

        services.AddSingleton(new SqliteMissionDb(missionDb));




        services.RemoveAll<IndexedDbReportRunner>();
        services.RemoveAll<IReportRunner>();
        services.RemoveAll<IReportSourceCache>();
        services.AddScoped<IReportSourceCache>(sp => {
            var cache = new ReportSourceCache(SqliteReportSource.Loader(
                sp.GetRequiredService<SqliteMissionDb>(), sp.GetRequiredService<IMissionStore>()));
            cache.AttachHub(sp.GetRequiredService<LedgerDataHub>());
            return cache;
        });
        services.AddScoped<IReportRunner>(sp => new SqliteReportRunner(
            sp.GetRequiredService<IReportSourceCache>(),
            sp.GetRequiredService<IWeightData>()));

        return services;
    }
}
