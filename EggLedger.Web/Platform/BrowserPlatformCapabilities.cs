namespace EggLedger.Web.Platform;

public sealed class BrowserPlatformCapabilities : IPlatformCapabilities {
    public bool IsDesktop => false;
    public Task OpenFileAsync(string path) => Task.CompletedTask;
    public Task OpenFileInFolderAsync(string path) => Task.CompletedTask;

    public Task OpenUrlAsync(string url) => Task.CompletedTask;

    public Task<string?> ChooseSaveFilePathAsync(string defaultName) => Task.FromResult<string?>(null);

    public Task RestartAppAsync() => Task.CompletedTask;

    public Task<(int w, int h)> GetWindowSizeAsync() => Task.FromResult((0, 0));

    public Task<string?> ChooseFolderAsync() => Task.FromResult<string?>(null);

    public Task SetFolderHiddenAsync(string path, bool hidden) => Task.CompletedTask;

    public string DataRootDir => "";
}
