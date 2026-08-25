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



//



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

        app.Run();
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
        if (Directory.Exists(wwwrootDir)) Directory.Delete(wwwrootDir, recursive: true);
        Directory.CreateDirectory(wwwrootDir);
        using (var archive = new System.IO.Compression.ZipArchive(zipStream, System.IO.Compression.ZipArchiveMode.Read)) {
            System.IO.Compression.ZipFileExtensions.ExtractToDirectory(archive, wwwrootDir, overwriteFiles: true);
        }
        File.WriteAllText(stamp, mvid);
    }
}
