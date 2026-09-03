using EggLedger.Desktop.Export;
using EggLedger.Desktop.Platform;
using EggLedger.Desktop.Storage;
using EggLedger.Desktop.Update;
using EggLedger.Web;
using EggLedger.Web.Data;
using EggLedger.Web.Platform;
using EggLedger.Web.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Photino.Blazor;

internal static class Program {
    [STAThread]
    private static void Main(string[] args) {
        var debugMode = args.Contains("--debug")
            || string.Equals(Environment.GetEnvironmentVariable("EGGLEDGER_DEBUG"), "1", StringComparison.Ordinal);



        var updateBootstrap = new UpdateBootstrap(new ProcessProbe(), new BinaryReplacement(new ProcessProbe()));
        updateBootstrap.RunStartup(args, Environment.ProcessPath);



        EnsureWwwrootExtracted();

        var appBuilder = PhotinoBlazorAppBuilder.CreateDefault(args);



        appBuilder.Services.AddEggLedgerWeb(CloudSyncBaseAddress());



        var dataRootDir = StoragePaths.ResolveDataRootDir(StoragePaths.DefaultRootDir());
        appBuilder.Services.AddDesktopSqliteStorage(dataRootDir);



        var desktopWindow = new PhotinoDesktopWindow();
        appBuilder.Services.AddDesktopPlatformCapabilities(new ProcessRunner(), desktopWindow);



        var runningVersion = AppVersionInfo.Current;
        appBuilder.Services.AddDesktopUpdater(() => runningVersion);



        appBuilder.Services.AddDesktopExportSink();


        appBuilder.Services.Configure<PhotinoBlazorAppConfiguration>(opts => opts.HostPage = "desktop.html");

        appBuilder.RootComponents.Add<App>("#app");

        var app = appBuilder.Build();



        desktopWindow.Attach(app.MainWindow);



        var settings = LoadDesktopSettings(app.Services);
        var width = settings.WindowWidth > 0 ? settings.WindowWidth : SettingsModel.DefaultWindowWidth;
        var height = settings.WindowHeight > 0 ? settings.WindowHeight : SettingsModel.DefaultWindowHeight;




        var iconFile = Path.Combine(AppContext.BaseDirectory, "icon-512.png");
        app.MainWindow.SetTitle("EggLedger");
        if (File.Exists(iconFile)) app.MainWindow.SetIconFile(iconFile);
        app.MainWindow
            .SetUseOsDefaultSize(false)
            .SetSize(width, height)
            .SetUseOsDefaultLocation(false)
            .Center();
        if (settings.StartInFullscreen) app.MainWindow.SetFullScreen(true);



        var platform = app.Services.GetRequiredService<IPlatformCapabilities>();
        app.MainWindow.RegisterWebMessageReceivedHandler((_, msg) => {
            if (msg.StartsWith("openurl:", StringComparison.Ordinal)) {
                _ = platform.OpenUrlAsync(msg["openurl:".Length..]);
                return;
            }
            if (debugMode) Log("WEBVIEW: " + msg);
        });

        if (debugMode) {
            app.MainWindow.SetDevToolsEnabled(true);
            app.MainWindow.SetLogVerbosity(2);


            AppDomain.CurrentDomain.FirstChanceException += (_, e) =>
                Log("FIRSTCHANCE: " + e.Exception.GetType().Name + ": " + e.Exception.Message);
            Log("debug mode on: devtools enabled (F12), WebView + managed errors logged");
        }



        AppDomain.CurrentDomain.UnhandledException += (_, error) =>
            Log("FATAL: " + (error.ExceptionObject.ToString() ?? "Unknown error"));

        StartMennoAutoRefresh(app.Services);
        StartGameEventsFeed(app.Services);

        app.Run();
    }

    private static void StartMennoAutoRefresh(IServiceProvider services) {
        _ = Task.Run(async () => {
            try {
                using var scope = services.CreateScope();
                var settings = scope.ServiceProvider.GetRequiredService<IndexedDbSettings>();
                var model = new SettingsModel();
                model.LoadFrom(await settings.GetAllSettingsAsync().ConfigureAwait(false));
                if (!model.AutoRefreshMenno) return;
                if (model.LastMennoRefreshAt is { } last && DateTimeOffset.UtcNow - last < TimeSpan.FromDays(5)) return;
                var menno = scope.ServiceProvider.GetRequiredService<EggLedger.Web.Services.MennoService>();
                await menno.RefreshAsync().ConfigureAwait(false);
                await settings.SetSettingAsync(
                    SettingsModel.KeyLastMennoRefresh,
                    DateTimeOffset.UtcNow.ToString("O")).ConfigureAwait(false);
            } catch (Exception ex) {
                Log("menno auto-refresh failed: " + ex.Message);
            }
        });
    }



    private static readonly TimeSpan GameEventsPollInterval = TimeSpan.FromMinutes(15);

    private static void StartGameEventsFeed(IServiceProvider services) {
        var events = services.GetRequiredService<EggLedger.Web.Services.GameEventsService>();
        if (!events.IsConfigured) return;
        _ = Task.Run(async () => {
            await PollGameEventsAsync(events, initial: true).ConfigureAwait(false);
            using var timer = new PeriodicTimer(GameEventsPollInterval);
            while (await timer.WaitForNextTickAsync().ConfigureAwait(false)) {
                await PollGameEventsAsync(events, initial: false).ConfigureAwait(false);
            }
        });
    }

    private static async Task PollGameEventsAsync(
        EggLedger.Web.Services.GameEventsService events, bool initial) {
        try {
            if (initial) {
                await events.EnsureLoadedAsync().ConfigureAwait(false);
            }
            await events.RefreshAsync().ConfigureAwait(false);
        } catch (Exception ex) {
            Log("game events poll failed: " + ex.Message);
        }
    }

    private static void Log(string text) {
        Console.Error.WriteLine(text);
        try {
            var logDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
            File.AppendAllText(Path.Combine(logDir, "EggLedger.fatal.log"), text + Environment.NewLine);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
        } catch (Exception ex) {

            Console.Error.WriteLine("failed to write fatal log: " + ex);
        }
    }


    private static Uri CloudSyncBaseAddress() => new("https://eggledger.davidarthurcole.me/");



    private static SettingsModel LoadDesktopSettings(IServiceProvider services) {
        var model = new SettingsModel();
        try {
            using var scope = services.CreateScope();
            var settings = scope.ServiceProvider.GetRequiredService<IndexedDbSettings>();
            model.LoadFrom(settings.GetAllSettingsAsync().GetAwaiter().GetResult());
        } catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or System.Text.Json.JsonException or IOException) {
        }
        return model;
    }



    private static void EnsureWwwrootExtracted() {
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        using var zipStream = assembly.GetManifestResourceStream("EggLedger.Desktop.wwwroot.zip");
        if (zipStream is null) return;
        var wwwrootDir = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        var stamp = Path.Combine(wwwrootDir, ".pack-stamp");
        var mvid = assembly.ManifestModule.ModuleVersionId.ToString();
        if (File.Exists(stamp) && File.ReadAllText(stamp) == mvid) return;

        CleanupLeftoverSwapDirs(wwwrootDir);

        var stagingDir = wwwrootDir + ".new-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(stagingDir);
        using (var archive = new System.IO.Compression.ZipArchive(zipStream, System.IO.Compression.ZipArchiveMode.Read)) {
            System.IO.Compression.ZipFileExtensions.ExtractToDirectory(archive, stagingDir, overwriteFiles: true);
        }
        File.WriteAllText(Path.Combine(stagingDir, ".pack-stamp"), mvid);

        var staleDir = wwwrootDir + ".stale-" + Guid.NewGuid().ToString("N");
        if (Directory.Exists(wwwrootDir) && !MoveDirectoryWithRetry(wwwrootDir, staleDir)) {
            MoveDirectoryWithRetry(stagingDir, wwwrootDir + "." + Guid.NewGuid().ToString("N"));
            return;
        }
        Directory.Move(stagingDir, wwwrootDir);
        if (Directory.Exists(staleDir)) {
            try {
                Directory.Delete(staleDir, recursive: true);
            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            }
        }
    }

    private static bool MoveDirectoryWithRetry(string src, string dst) {
        for (var i = 0; i < 10; i++) {
            try {
                Directory.Move(src, dst);
                return true;
            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
                Thread.Sleep(300);
            }
        }
        return false;
    }

    private static void CleanupLeftoverSwapDirs(string wwwrootDir) {
        var exeDir = Path.GetDirectoryName(wwwrootDir);
        if (string.IsNullOrEmpty(exeDir)) return;
        var wwwrootName = Path.GetFileName(wwwrootDir);
        string[] matches;
        try {
            matches = Directory.GetDirectories(exeDir, wwwrootName + ".*");
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException) {
            return;
        }
        foreach (var match in matches) {
            try {
                Directory.Delete(match, recursive: true);
            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            }
        }
    }
}
