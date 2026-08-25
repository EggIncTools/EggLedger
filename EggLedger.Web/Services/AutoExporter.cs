using EggLedger.Domain.Export;
using EggLedger.Domain.MissionQuery;
using EggLedger.Web.Data;
using EggLedger.Web.Settings;

namespace EggLedger.Web.Services;

public interface IAutoExporter {
    Task RunAfterFetchAsync(string accountId, CancellationToken cancellationToken = default);
}

public abstract class AutoExporterBase : IAutoExporter {
    private readonly IndexedDbSettings _settings;
    private readonly IMissionStore _store;
    private readonly MissionQueryHandlers _queries;

    protected AutoExporterBase(IndexedDbSettings settings, IMissionStore store, MissionQueryHandlers queries) {
        _settings = settings;
        _store = store;
        _queries = queries;
    }

    public async Task RunAfterFetchAsync(string accountId, CancellationToken cancellationToken = default) {
        var all = await _settings.GetAllSettingsAsync().ConfigureAwait(false);
        var model = new SettingsModel();
        model.LoadFrom(all);
        if (!model.AutoExportCsv && !model.AutoExportXlsx) {
            return;
        }

        var responses = await _store.GetPlayerCompleteMissionsAsync(accountId).ConfigureAwait(false);
        if (responses is null || responses.Count == 0) {
            return;
        }

        var missions = responses.Select(Mission.FromResponse).ToList();
        var accounts = await _queries.GetExistingDataAsync().ConfigureAwait(false);
        var nickname = accounts.FirstOrDefault(a => a.Id == accountId)?.Nickname ?? "";
        await DeliverAsync(accountId, nickname, missions, model, cancellationToken).ConfigureAwait(false);
    }

    protected abstract Task DeliverAsync(
        string accountId,
        string nickname,
        IReadOnlyList<Mission> missions,
        SettingsModel model,
        CancellationToken cancellationToken);

    protected static string SanitizeName(string name) {
        if (string.IsNullOrWhiteSpace(name)) {
            return "account";
        }

        var chars = name.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
        return new string(chars);
    }
}

public sealed class BrowserAutoExporter : AutoExporterBase {
    private readonly IDownloadService _downloads;

    public BrowserAutoExporter(
        IndexedDbSettings settings, IMissionStore store, MissionQueryHandlers queries, IDownloadService downloads)
        : base(settings, store, queries) {
        _downloads = downloads;
    }

    protected override async Task DeliverAsync(
        string accountId,
        string nickname,
        IReadOnlyList<Mission> missions,
        SettingsModel model,
        CancellationToken cancellationToken) {
        var baseName = $"{SanitizeName(nickname)}_{accountId}";
        if (model.AutoExportCsv) {
            await _downloads.DownloadCsvAsync(missions, baseName + ".csv").ConfigureAwait(false);
        }

        if (model.AutoExportXlsx) {
            await _downloads.DownloadXlsxAsync(missions, baseName + ".xlsx").ConfigureAwait(false);
        }
    }
}
