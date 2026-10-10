using EggLedger.Domain.Export;

namespace EggLedger.Web.Services;

public interface IDownloadService {
    ValueTask DownloadCsvAsync(IReadOnlyList<Mission> missions, string filename);
    ValueTask DownloadXlsxAsync(IReadOnlyList<Mission> missions, string filename);

    ValueTask DownloadJsonAsync(string json, string filename);
}
