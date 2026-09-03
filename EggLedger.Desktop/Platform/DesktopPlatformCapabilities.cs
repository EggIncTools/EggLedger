using System.Runtime.InteropServices;
using EggLedger.Desktop.Storage;
using EggLedger.Web.Platform;

namespace EggLedger.Desktop.Platform;

public sealed class DesktopPlatformCapabilities(IProcessRunner processRunner, IDesktopWindow window) : IPlatformCapabilities {
    private readonly IProcessRunner _processRunner = processRunner;
    private readonly IDesktopWindow _window = window;

    public bool IsDesktop => true;

    public Task OpenFileAsync(string path) {

        var (exe, args) = DesktopCommandBuilder.BuildOpenCommand(CurrentPlatform(), Path.GetFullPath(path));
        return _processRunner.RunAsync(exe, args);
    }

    public Task OpenUrlAsync(string url) {

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) {
            return Task.CompletedTask;
        }
        var (exe, args) = DesktopCommandBuilder.BuildOpenCommand(CurrentPlatform(), uri.AbsoluteUri);
        return _processRunner.RunAsync(exe, args);
    }

    public Task OpenFileInFolderAsync(string path) {
        var (exe, args) = DesktopCommandBuilder.BuildOpenInFolderCommand(CurrentPlatform(), Path.GetFullPath(path));
        return _processRunner.RunAsync(exe, args);
    }

    public Task<string?> ChooseSaveFilePathAsync(string defaultName)
        => Task.FromResult(_window.ShowSaveFileDialog(defaultName));

    public async Task RestartAppAsync() {
        var exePath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(exePath)) {
            var (exe, args) = DesktopCommandBuilder.BuildRestartCommand(exePath);
            await _processRunner.RunAsync(exe, args);
        }
        _window.ExitProcess();
    }

    public Task<(int w, int h)> GetWindowSizeAsync() {
        var (width, height) = _window.GetSize();
        return Task.FromResult((width, height));
    }

    public Task<string?> ChooseFolderAsync() => Task.FromResult(_window.ShowOpenFolderDialog());

    public Task SetFolderHiddenAsync(string path, bool hidden) {
        var full = Path.GetFullPath(path);
        if (CurrentPlatform() == OSPlatform.Windows) {
            var attrs = File.GetAttributes(full);
            File.SetAttributes(full, hidden ? attrs | FileAttributes.Hidden : attrs & ~FileAttributes.Hidden);
            return Task.CompletedTask;
        }
        var cmd = DesktopCommandBuilder.BuildSetHiddenCommand(CurrentPlatform(), full, hidden);
        return cmd is { } c ? _processRunner.RunAsync(c.Exe, c.Args) : Task.CompletedTask;
    }

    public string DataRootDir => StoragePaths.ResolveDataRootDir(StoragePaths.DefaultRootDir());

    private static OSPlatform CurrentPlatform() {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            return OSPlatform.Windows;
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
            return OSPlatform.OSX;
        }
        return OSPlatform.Linux;
    }
}
