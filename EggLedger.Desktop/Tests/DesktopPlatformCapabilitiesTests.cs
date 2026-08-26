using System.Runtime.InteropServices;
using EggLedger.Desktop.Platform;

namespace EggLedger.Desktop.Tests;

public sealed class DesktopPlatformCapabilitiesTests {
    private sealed class FakeProcessRunner : IProcessRunner {
        public List<(string Exe, IReadOnlyList<string> Args)> Calls { get; } = [];

        public Task RunAsync(string exe, IReadOnlyList<string> args) {
            Calls.Add((exe, args));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeWindow : IDesktopWindow {
        public (int Width, int Height) SizeToReturn { get; set; } = (0, 0);
        public string? SaveResult { get; set; }
        public int ExitCalls { get; private set; }
        public string? OpenFolderResult { get; set; }
        public (int Width, int Height)? LastSetSize { get; private set; }
        public bool? LastFullScreen { get; private set; }
        public (int Width, int Height) GetSize() => SizeToReturn;
        public string? ShowSaveFileDialog(string defaultName) => SaveResult;
        public void ExitProcess() => ExitCalls++;
        public string? ShowOpenFolderDialog() => OpenFolderResult;
        public void SetSize(int width, int height) => LastSetSize = (width, height);
        public void SetFullScreen(bool fullScreen) => LastFullScreen = fullScreen;
    }

    [Fact]
    public async Task OpenFileAsync_RunsCurrentPlatformOpenCommand() {
        var runner = new FakeProcessRunner();
        var caps = new DesktopPlatformCapabilities(runner, new FakeWindow());

        var path = OperatingSystem.IsWindows() ? @"C:\data\file.json" : "/data/file.json";
        await caps.OpenFileAsync(path);

        var call = Assert.Single(runner.Calls);
        var expected = DesktopCommandBuilder.BuildOpenCommand(CurrentPlatform(), path);
        Assert.Equal(expected.Exe, call.Exe);
        Assert.Equal(expected.Args, call.Args);
    }

    [Fact]
    public async Task OpenFileInFolderAsync_RunsCurrentPlatformRevealCommand() {
        var runner = new FakeProcessRunner();
        var caps = new DesktopPlatformCapabilities(runner, new FakeWindow());

        var path = OperatingSystem.IsWindows() ? @"C:\data\file.json" : "/data/file.json";
        await caps.OpenFileInFolderAsync(path);

        var call = Assert.Single(runner.Calls);
        var expected = DesktopCommandBuilder.BuildOpenInFolderCommand(CurrentPlatform(), path);
        Assert.Equal(expected.Exe, call.Exe);
        Assert.Equal(expected.Args, call.Args);
    }

    [Fact]
    public async Task ChooseSaveFilePathAsync_ReturnsNull_WhenDialogCancelled() {
        var caps = new DesktopPlatformCapabilities(new FakeProcessRunner(), new FakeWindow { SaveResult = null });
        var result = await caps.ChooseSaveFilePathAsync("export.json");
        Assert.Null(result);
    }

    [Fact]
    public async Task ChooseSaveFilePathAsync_ReturnsChosenPath() {
        var window = new FakeWindow { SaveResult = "/chosen/export.json" };
        var caps = new DesktopPlatformCapabilities(new FakeProcessRunner(), window);
        var result = await caps.ChooseSaveFilePathAsync("export.json");
        Assert.Equal("/chosen/export.json", result);
    }

    [Fact]
    public async Task GetWindowSizeAsync_PassesThroughWindowSize() {
        var window = new FakeWindow { SizeToReturn = (1280, 800) };
        var caps = new DesktopPlatformCapabilities(new FakeProcessRunner(), window);
        var (w, h) = await caps.GetWindowSizeAsync();
        Assert.Equal(1280, w);
        Assert.Equal(800, h);
    }

    [Fact]
    public async Task RestartAppAsync_RelaunchesExe_ThenExits() {
        var runner = new FakeProcessRunner();
        var window = new FakeWindow();
        var caps = new DesktopPlatformCapabilities(runner, window);

        await caps.RestartAppAsync();


        var call = Assert.Single(runner.Calls);
        Assert.Equal(Environment.ProcessPath, call.Exe);
        Assert.Empty(call.Args);
        Assert.Equal(1, window.ExitCalls);
    }

    [Theory]
    [InlineData("https://example.com/x")]
    [InlineData("http://example.com/y")]
    public async Task OpenUrlAsync_RunsOpenCommandForHttpUrls(string url) {
        var runner = new FakeProcessRunner();
        var caps = new DesktopPlatformCapabilities(runner, new FakeWindow());

        await caps.OpenUrlAsync(url);

        var call = Assert.Single(runner.Calls);
        var expected = DesktopCommandBuilder.BuildOpenCommand(CurrentPlatform(), url);
        Assert.Equal(expected.Exe, call.Exe);
        Assert.Equal(expected.Args, call.Args);
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://host/f")]
    [InlineData("not a url")]
    public async Task OpenUrlAsync_IgnoresNonHttpSchemes(string url) {
        var runner = new FakeProcessRunner();
        var caps = new DesktopPlatformCapabilities(runner, new FakeWindow());

        await caps.OpenUrlAsync(url);

        Assert.Empty(runner.Calls);
    }

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
