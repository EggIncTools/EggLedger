using EggLedger.Desktop.Platform;
using EggLedger.Desktop.Storage;
using EggLedger.Domain.Api;
using EggLedger.Web;
using Microsoft.Extensions.DependencyInjection;

namespace EggLedger.Desktop.Tests;

public class ApiClientRegistrationTests {
    [Fact]
    public void DesktopApiClient_CallsAuxbrainDirect_NotEggApiProxy() {
        var services = new ServiceCollection();

        services.AddEggLedgerWeb(new Uri("https://eggledger.egginc.tools/"));
        var missionDb = SqliteDatabase.OpenMissionDb(":memory:");
        var reportDb = SqliteDatabase.OpenReportDb(":memory:");
        services.AddDesktopSqliteStorage(missionDb, reportDb);
        services.AddDesktopPlatformCapabilities(new ProcessRunner(), new FakeDesktopWindow());

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var api = scope.ServiceProvider.GetRequiredService<ApiClient>();

        Assert.Equal(ApiClient.DefaultApiPrefix, api.ApiPrefix);
        Assert.DoesNotContain("egg-api", api.ApiPrefix);
    }

    private sealed class FakeDesktopWindow : IDesktopWindow {
        public (int Width, int Height) GetSize() => (100, 100);
        public string? ShowSaveFileDialog(string defaultName) => null;
        public void ExitProcess() { }
        public string? ShowOpenFolderDialog() => null;
        public void SetSize(int width, int height) { }
        public void SetFullScreen(bool fullScreen) { }
    }
}
