using EggLedger.Domain.Export;
using EggLedger.Web.Services;
using EggLedger.Web.Tests.Data;
using Microsoft.Extensions.Logging.Abstractions;

namespace EggLedger.Web.Tests.Services;

public sealed class DownloadServiceTests {
    private static (DownloadService Service, FakeJsRuntime Runtime) Make() {
        var runtime = new FakeJsRuntime(new FakeJsObjectReference());
        return (new DownloadService(runtime, NullLogger<DownloadService>.Instance), runtime);
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
    public async Task DownloadCsvAsync_forwards_csv_bytes_and_mime() {
        var (service, runtime) = Make();
        var missions = CannedMissions();

        await service.DownloadCsvAsync(missions, "missions.csv");

        var (_, args) = Assert.Single(runtime.Calls, c => c.Identifier == "eggDownload");
        Assert.Equal("missions.csv", args[0]);
        Assert.Equal("text/csv", args[1]);
        Assert.Equal(MissionExport.MissionsToCsvBytes(missions), args[2]);
    }

    [Fact]
    public async Task DownloadXlsxAsync_forwards_xlsx_bytes_and_mime() {
        var (service, runtime) = Make();
        var missions = CannedMissions();

        await service.DownloadXlsxAsync(missions, "missions.xlsx");

        var (_, args) = Assert.Single(runtime.Calls, c => c.Identifier == "eggDownload");
        Assert.Equal("missions.xlsx", args[0]);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", args[1]);
        Assert.Equal(MissionExport.MissionsToXlsxBytes(missions), args[2]);
    }
}
