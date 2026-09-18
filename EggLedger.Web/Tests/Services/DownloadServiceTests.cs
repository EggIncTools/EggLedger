using EggLedger.Domain.Export;
using EggLedger.Web.Services;
using EggLedger.Web.Tests.Data;

namespace EggLedger.Web.Tests.Services;

public sealed class DownloadServiceTests {
    private static (DownloadService Service, FakeJsObjectReference Module) Make() {
        var module = new FakeJsObjectReference();
        var runtime = new FakeJsRuntime(module);
        return (new DownloadService(runtime), module);
    }

    private static IReadOnlyList<Mission> CannedMissions() => new[] {
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
    };

    [Fact]
    public async Task DownloadCsvAsync_forwards_download_with_csv_bytes_and_mime() {
        var (service, module) = Make();
        var missions = CannedMissions();
        var expectedBase64 = Convert.ToBase64String(MissionExport.MissionsToCsvBytes(missions));

        await service.DownloadCsvAsync(missions, "missions.csv");

        var call = Assert.Single(module.Calls);
        Assert.Equal("download", call.Identifier);
        Assert.Equal("missions.csv", call.Args[0]);
        Assert.Equal(expectedBase64, call.Args[1]);
        Assert.Equal("text/csv", call.Args[2]);
    }

    [Fact]
    public async Task DownloadXlsxAsync_forwards_download_with_xlsx_bytes_and_mime() {
        var (service, module) = Make();
        var missions = CannedMissions();
        var expectedBase64 = Convert.ToBase64String(MissionExport.MissionsToXlsxBytes(missions));

        await service.DownloadXlsxAsync(missions, "missions.xlsx");

        var call = Assert.Single(module.Calls);
        Assert.Equal("download", call.Identifier);
        Assert.Equal("missions.xlsx", call.Args[0]);
        Assert.Equal(expectedBase64, call.Args[1]);
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            call.Args[2]);
    }

    [Fact]
    public async Task Module_imported_once_across_calls() {
        var module = new FakeJsObjectReference();
        var runtime = new FakeJsRuntime(module);
        var service = new DownloadService(runtime);
        var missions = CannedMissions();

        await service.DownloadCsvAsync(missions, "a.csv");
        await service.DownloadXlsxAsync(missions, "b.xlsx");

        Assert.Single(runtime.Calls, c => c.Identifier == "import");
    }
}
