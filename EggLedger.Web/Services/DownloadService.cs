using System.Text;
using EggIdentity.UI;
using EggLedger.Domain.Export;

namespace EggLedger.Web.Services;

public sealed class DownloadService(Downloads downloads) : IDownloadService {
    private const string CsvMime = "text/csv";
    private const string XlsxMime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const string JsonMime = "application/json";

    public ValueTask DownloadCsvAsync(IReadOnlyList<Mission> missions, string filename) {
        downloads.SaveBytes(filename, MissionExport.MissionsToCsvBytes(missions), CsvMime);
        return ValueTask.CompletedTask;
    }

    public ValueTask DownloadXlsxAsync(IReadOnlyList<Mission> missions, string filename) {
        downloads.SaveBytes(filename, MissionExport.MissionsToXlsxBytes(missions), XlsxMime);
        return ValueTask.CompletedTask;
    }

    public ValueTask DownloadJsonAsync(string json, string filename) {
        downloads.SaveBytes(filename, Encoding.UTF8.GetBytes(json), JsonMime);
        return ValueTask.CompletedTask;
    }
}
