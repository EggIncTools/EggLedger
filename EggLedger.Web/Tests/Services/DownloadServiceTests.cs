using EggIdentity.UI;
using EggLedger.Domain.Export;
using EggLedger.Web.Services;

namespace EggLedger.Web.Tests.Services;

public sealed class DownloadServiceTests {
    private static (DownloadService Service, Downloads Downloads, DownloadStore Store) Make() {
        var store = new DownloadStore(TimeProvider.System);
        var downloads = new Downloads(store, new DownloadOptions());
        return (new DownloadService(downloads), downloads, store);
    }

    private static PendingDownload TakeSingle(Downloads downloads, DownloadStore store) {
        var id = Assert.Single(downloads.Pending).Split('/')[^1];
        return Assert.IsType<PendingDownload>(store.Take(id));
    }

    private static IReadOnlyList<Mission> CannedMissions() => [
        new Mission {
            Id = "m1",
            TypeName = "Standard",
            ShipName = "Chicken One",
            DurationTypeName = "Short",
            Level = 1,
            LaunchedAt = new DateTimeOffset(2023, 1, 1, 12, 0, 0, TimeSpan.Zero),
            LaunchedAtStr = "2023-01-01T12:00:00Z",
            ReturnedAt = new DateTimeOffset(2023, 1, 1, 14, 0, 0, TimeSpan.Zero),
            ReturnedAtStr = "2023-01-01T14:00:00Z",
            DurationDays = 2.0 / 24.0,
            Capacity = 50,
        },
    ];

    [Fact]
    public async Task DownloadCsvAsync_queues_csv_bytes_and_mime() {
        var (service, downloads, store) = Make();
        var missions = CannedMissions();

        await service.DownloadCsvAsync(missions, "missions.csv");

        var item = TakeSingle(downloads, store);
        Assert.Equal("missions.csv", item.FileName);
        Assert.Equal("text/csv", item.ContentType);
        Assert.Equal(MissionExport.MissionsToCsvBytes(missions), item.Bytes.ToArray());
    }

    [Fact]
    public async Task DownloadXlsxAsync_queues_xlsx_bytes_and_mime() {
        var (service, downloads, store) = Make();
        var missions = CannedMissions();

        await service.DownloadXlsxAsync(missions, "missions.xlsx");

        var item = TakeSingle(downloads, store);
        Assert.Equal("missions.xlsx", item.FileName);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", item.ContentType);
        Assert.Equal(MissionExport.MissionsToXlsxBytes(missions), item.Bytes.ToArray());
    }

    [Fact]
    public async Task DownloadJsonAsync_queues_utf8_json() {
        var (service, downloads, store) = Make();

        await service.DownloadJsonAsync("{\"a\":1}", "reports.json");

        var item = TakeSingle(downloads, store);
        Assert.Equal("reports.json", item.FileName);
        Assert.Equal("application/json", item.ContentType);
        Assert.Equal("{\"a\":1}", System.Text.Encoding.UTF8.GetString(item.Bytes.ToArray()));
    }
}
