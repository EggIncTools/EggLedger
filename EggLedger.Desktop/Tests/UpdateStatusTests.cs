using System.Formats.Tar;
using System.IO.Compression;
using EggLedger.Desktop.Platform;
using EggLedger.Desktop.Update;
using EggLedger.Web.Data;
using EggLedger.Web.Platform;

namespace EggLedger.Desktop.Tests;

public sealed class UpdateStatusTests {
    private sealed class RecordingRunner : IProcessRunner {
        public string? Exe { get; private set; }
        public IReadOnlyList<string>? Args { get; private set; }

        public Task RunAsync(string exe, IReadOnlyList<string> args) {
            Exe = exe;
            Args = args;
            return Task.CompletedTask;
        }
    }

    private static byte[] AssetPayload(string assetName) {
        var binary = new byte[1024];
        if (assetName.EndsWith(".tar.gz", StringComparison.Ordinal)) {
            using var ms = new MemoryStream();
            using (var gz = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true))
            using (var tar = new TarWriter(gz, TarEntryFormat.Pax)) {
                tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "EggLedger") {
                    DataStream = new MemoryStream(binary),
                });
            }
            return ms.ToArray();
        }
        if (assetName.EndsWith(".zip", StringComparison.Ordinal)) {
            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true)) {
                using var s = zip.CreateEntry("EggLedger").Open();
                s.Write(binary);
            }
            return ms.ToArray();
        }
        return binary;
    }

    private static GithubReleaseClient Github(Func<HttpRequestMessage, HttpResponseMessage> responder)
        => new(new HttpClient(new StubHttpMessageHandler(responder)));

    private sealed class CountingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            Calls++;
            return Task.FromResult(responder(request));
        }
    }

    private sealed class InMemorySettingsDb : IIndexedDb {
        private readonly Dictionary<string, SettingRow> _rows = [];

        public ValueTask PutAsync(string store, object value) {
            var row = (SettingRow)value;
            _rows[row.Key] = row;
            return ValueTask.CompletedTask;
        }

        public ValueTask<int> PutManyAsync(string store, IEnumerable<object> values) {
            var n = 0;
            foreach (var v in values) {
                var row = (SettingRow)v;
                _rows[row.Key] = row;
                n++;
            }
            return new ValueTask<int>(n);
        }

        public ValueTask<T[]> GetAllAsync<T>(string store)
            => new([.. _rows.Values.Cast<T>()]);

        public ValueTask<T?> GetAsync<T>(string store, object key) => throw new NotSupportedException();
        public ValueTask<T[]> GetAllByIndexAsync<T>(string store, string index, object value) => throw new NotSupportedException();
        public ValueTask<T[]> GetAllByIndexProjectedAsync<T>(string store, string index, object value) => throw new NotSupportedException();
        public ValueTask DeleteAsync(string store, object key) => throw new NotSupportedException();
        public ValueTask ClearAsync(string store) => throw new NotSupportedException();
        public ValueTask<int> CountAsync(string store) => throw new NotSupportedException();
    }

    [Fact]
    public void StatusFile_RoundTrips_AndClears() {
        var dir = Path.Combine(Path.GetTempPath(), "egg-status-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try {
            Assert.Null(UpdateStatusFile.ReadAndClear(dir));

            UpdateStatusFile.Write(dir, new UpdateStatus { Success = true, ToVersion = "2.2.0" });

            var s = UpdateStatusFile.ReadAndClear(dir);
            Assert.NotNull(s);
            Assert.True(s!.Success);
            Assert.Equal("2.2.0", s.ToVersion);

            Assert.Null(UpdateStatusFile.ReadAndClear(dir));
        } finally {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task CheckForUpdates_NewerLatest_SetsAvailable() {
        var github = Github(_ => StubHttpMessageHandler.Json("""{"tag_name":"2.5.0","body":"new"}"""));
        var svc = new UpdateService(github, () => "2.1.4");

        await svc.CheckForUpdatesAsync();

        Assert.Equal(UpdatePhase.Available, svc.Phase);
        Assert.Equal("2.5.0", svc.AvailableVersion);
        Assert.Equal("new", svc.ReleaseNotes);
    }

    [Fact]
    public async Task CheckForUpdates_SameVersion_UpToDate() {
        var github = Github(_ => StubHttpMessageHandler.Json("""{"tag_name":"2.1.4","body":""}"""));
        var svc = new UpdateService(github, () => "2.1.4");

        await svc.CheckForUpdatesAsync();

        Assert.Equal(UpdatePhase.UpToDate, svc.Phase);
        Assert.Null(svc.AvailableVersion);
    }

    [Fact]
    public async Task CheckForUpdates_OlderLatest_UpToDate() {
        var github = Github(_ => StubHttpMessageHandler.Json("""{"tag_name":"2.0.0","body":""}"""));
        var svc = new UpdateService(github, () => "2.1.4");

        await svc.CheckForUpdatesAsync();

        Assert.Equal(UpdatePhase.UpToDate, svc.Phase);
    }

    [Fact]
    public async Task CheckForUpdates_RunningNewerThanStable_ChecksPreReleases() {

        var stableJson = """{"tag_name":"2.5.0","body":"stable"}""";
        var preJson = """[{"tag_name":"2.7.0-rc.1","body":"rc","draft":false}]""";
        var github = Github(req =>
            req.RequestUri!.AbsoluteUri.Contains("releases/latest")
                ? StubHttpMessageHandler.Json(stableJson)
                : StubHttpMessageHandler.Json(preJson));
        var svc = new UpdateService(github, () => "2.6.0");

        await svc.CheckForUpdatesAsync();

        Assert.Equal(UpdatePhase.Available, svc.Phase);
        Assert.Equal("2.7.0-rc.1", svc.AvailableVersion);
    }

    [Fact]
    public async Task CheckForUpdates_FetchFails_Failed() {
        var github = Github(_ => new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        var svc = new UpdateService(github, () => "2.1.4");

        await svc.CheckForUpdatesAsync();

        Assert.Equal(UpdatePhase.Failed, svc.Phase);
    }

    [Fact]
    public async Task DownloadAndInstall_WritesNewBinaryAndGoesReady() {
        var exeDir = Path.Combine(Path.GetTempPath(), "egg-dl-svc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(exeDir);
        var exePath = Path.Combine(exeDir, OperatingSystem.IsWindows() ? "EggLedger.exe" : "EggLedger");
        try {
            var want = GithubReleaseClient.ExpectedAssetName();
            var assetJson = $$"""{"assets":[{"name":"{{want}}","browser_download_url":"https://x/{{want}}"}]}""";
            var payload = AssetPayload(want);
            var github = Github(req =>
                req.RequestUri!.AbsoluteUri.Contains("releases/tags")
                    ? StubHttpMessageHandler.Json(assetJson)
                    : StubHttpMessageHandler.Bytes(payload));

            var svc = new UpdateService(github, () => "2.1.4", () => exePath);
            await svc.DownloadAndInstallAsync("2.2.0");

            Assert.Equal(UpdatePhase.Ready, svc.Phase);
            var tempPath = UpdateService.NewBinaryTempPath(exePath);
            Assert.True(File.Exists(tempPath), "downloaded _new binary should exist");
        } finally {
            Directory.Delete(exeDir, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadAndInstall_ValidChecksum_GoesReady() {
        var exeDir = Path.Combine(Path.GetTempPath(), "egg-dl-sum-ok-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(exeDir);
        var exePath = Path.Combine(exeDir, OperatingSystem.IsWindows() ? "EggLedger.exe" : "EggLedger");
        try {
            var want = GithubReleaseClient.ExpectedAssetName();
            var payload = AssetPayload(want);
            var sum = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(payload));
            var assetJson = $$"""{"assets":[{"name":"{{want}}","browser_download_url":"https://x/{{want}}"},{"name":"SHA256SUMS","browser_download_url":"https://x/SHA256SUMS"}]}""";
            var github = Github(req =>
                req.RequestUri!.AbsoluteUri.Contains("releases/tags") ? StubHttpMessageHandler.Json(assetJson)
                : req.RequestUri!.AbsoluteUri.EndsWith("SHA256SUMS", StringComparison.Ordinal)
                    ? StubHttpMessageHandler.Json($"{sum}  {want}\n")
                    : StubHttpMessageHandler.Bytes(payload));

            var svc = new UpdateService(github, () => "2.1.4", () => exePath);
            await svc.DownloadAndInstallAsync("2.2.0");

            Assert.Equal(UpdatePhase.Ready, svc.Phase);
        } finally {
            Directory.Delete(exeDir, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadAndInstall_ChecksumMismatch_Fails() {
        var exeDir = Path.Combine(Path.GetTempPath(), "egg-dl-sum-bad-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(exeDir);
        var exePath = Path.Combine(exeDir, OperatingSystem.IsWindows() ? "EggLedger.exe" : "EggLedger");
        try {
            var want = GithubReleaseClient.ExpectedAssetName();
            var payload = AssetPayload(want);
            var wrong = new string('0', 64);
            var assetJson = $$"""{"assets":[{"name":"{{want}}","browser_download_url":"https://x/{{want}}"},{"name":"SHA256SUMS","browser_download_url":"https://x/SHA256SUMS"}]}""";
            var github = Github(req =>
                req.RequestUri!.AbsoluteUri.Contains("releases/tags") ? StubHttpMessageHandler.Json(assetJson)
                : req.RequestUri!.AbsoluteUri.EndsWith("SHA256SUMS", StringComparison.Ordinal)
                    ? StubHttpMessageHandler.Json($"{wrong}  {want}\n")
                    : StubHttpMessageHandler.Bytes(payload));

            var svc = new UpdateService(github, () => "2.1.4", () => exePath);
            await svc.DownloadAndInstallAsync("2.2.0");

            Assert.Equal(UpdatePhase.Failed, svc.Phase);
            Assert.False(File.Exists(UpdateService.NewBinaryTempPath(exePath)), "binary should be deleted on checksum failure");
        } finally {
            Directory.Delete(exeDir, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadAndInstall_ReachesHandoff_LaunchesNewAndExits() {

        var exeDir = Path.Combine(Path.GetTempPath(), "egg-dl-handoff-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(exeDir);
        var exePath = Path.Combine(exeDir, OperatingSystem.IsWindows() ? "EggLedger.exe" : "EggLedger");
        try {
            var want = GithubReleaseClient.ExpectedAssetName();
            var assetJson = $$"""{"assets":[{"name":"{{want}}","browser_download_url":"https://x/{{want}}"}]}""";
            var payload = AssetPayload(want);
            var github = Github(req =>
                req.RequestUri!.AbsoluteUri.Contains("releases/tags")
                    ? StubHttpMessageHandler.Json(assetJson)
                    : StubHttpMessageHandler.Bytes(payload));

            var runner = new RecordingRunner();
            var exited = false;
            var svc = new UpdateService(
                github,
                () => "2.1.4",
                () => exePath,
                runner,
                exitAction: () => {
                    exited = true;
                    return Task.CompletedTask;
                },
                handshakeTimeout: TimeSpan.FromMilliseconds(50),
                exitDelay: TimeSpan.Zero);

            await svc.DownloadAndInstallAsync("2.2.0");

            Assert.Equal(UpdatePhase.Ready, svc.Phase);
            var tempPath = UpdateService.NewBinaryTempPath(exePath);
            Assert.Equal(tempPath, runner.Exe);
            Assert.NotNull(runner.Args);
            Assert.Contains(runner.Args!, a => a.StartsWith("--replace-pid=", StringComparison.Ordinal));
            Assert.Contains(runner.Args!, a => a.StartsWith("--replace-path=", StringComparison.Ordinal));
            Assert.Contains(runner.Args!, a => a.StartsWith("--handshake-port=", StringComparison.Ordinal));
            Assert.Contains(runner.Args!, a => a.StartsWith("--handshake-token=", StringComparison.Ordinal));
            Assert.True(exited, "DownloadAndInstall should reach the handoff and run the exit action");
        } finally {
            Directory.Delete(exeDir, recursive: true);
        }
    }

    [Theory]
    [InlineData("127.0.0.1:5000", "tok", true)]
    [InlineData(null, null, false)]
    public void BuildReplaceArgs_IncludesHandshakeOnlyWhenPresent(string? addr, string? token, bool hasHandshake) {
        var args = UpdateService.BuildReplaceArgs(1234, @"C:\app\EggLedger.exe", addr, token);

        Assert.Contains("--replace-pid=1234", args);
        Assert.Contains(@"--replace-path=C:\app\EggLedger.exe", args);
        if (hasHandshake) {
            Assert.Contains($"--handshake-port={addr}", args);
            Assert.Contains($"--handshake-token={token}", args);
        } else {
            Assert.DoesNotContain(args, a => a.StartsWith("--handshake-port", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task Cooldown_FirstCheckEver_PollsAndPersistsSnapshot() {
        var settingsDb = new InMemorySettingsDb();
        var settings = new IndexedDbSettings(settingsDb);
        var now = DateTimeOffset.Parse("2026-06-23T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var handler = new CountingHandler(_ => StubHttpMessageHandler.Json("""{"tag_name":"2.5.0","body":"new"}"""));
        var github = new GithubReleaseClient(new HttpClient(handler));
        var svc = new UpdateService(github, () => "2.1.4", settings: settings, now: () => now);

        await svc.CheckForUpdatesAsync();

        Assert.Equal(1, handler.Calls);
        Assert.Equal(UpdatePhase.Available, svc.Phase);
        Assert.Equal("2.5.0", svc.AvailableVersion);

        var stored = await settings.GetAllSettingsAsync();
        Assert.Equal("2.5.0", stored[UpdateService.KnownLatestVersionKey]);
        Assert.Equal("new", stored[UpdateService.KnownLatestReleaseNotesKey]);
        Assert.True(stored.ContainsKey(UpdateService.LastUpdateCheckAtKey));
    }

    [Fact]
    public async Task Cooldown_WithinWindowNotForced_SkipsPoll_UpToDateFromSnapshot() {
        var settingsDb = new InMemorySettingsDb();
        var settings = new IndexedDbSettings(settingsDb);
        var now = DateTimeOffset.Parse("2026-06-23T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

        await settings.SetSettingsAsync(new Dictionary<string, string> {
            [UpdateService.LastUpdateCheckAtKey] = now.AddHours(-1).ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            [UpdateService.KnownLatestVersionKey] = "2.1.4",
            [UpdateService.KnownLatestReleaseNotesKey] = "",
        });
        var handler = new CountingHandler(_ => StubHttpMessageHandler.Json("""{"tag_name":"9.9.9","body":"should-not-be-fetched"}"""));
        var github = new GithubReleaseClient(new HttpClient(handler));
        var svc = new UpdateService(github, () => "2.1.4", settings: settings, now: () => now);

        await svc.CheckForUpdatesAsync();

        Assert.Equal(0, handler.Calls);
        Assert.Equal(UpdatePhase.UpToDate, svc.Phase);
        Assert.Null(svc.AvailableVersion);
    }

    [Fact]
    public async Task Cooldown_WithinWindow_StoredLatestNewer_AvailableFromSnapshot_NoPoll() {
        var settingsDb = new InMemorySettingsDb();
        var settings = new IndexedDbSettings(settingsDb);
        var now = DateTimeOffset.Parse("2026-06-23T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        await settings.SetSettingsAsync(new Dictionary<string, string> {
            [UpdateService.LastUpdateCheckAtKey] = now.AddHours(-2).ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            [UpdateService.KnownLatestVersionKey] = "2.5.0",
            [UpdateService.KnownLatestReleaseNotesKey] = "cached notes",
        });
        var handler = new CountingHandler(_ => StubHttpMessageHandler.Json("""{"tag_name":"9.9.9","body":"x"}"""));
        var github = new GithubReleaseClient(new HttpClient(handler));
        var svc = new UpdateService(github, () => "2.1.4", settings: settings, now: () => now);

        await svc.CheckForUpdatesAsync();

        Assert.Equal(0, handler.Calls);
        Assert.Equal(UpdatePhase.Available, svc.Phase);
        Assert.Equal("2.5.0", svc.AvailableVersion);
        Assert.Equal("cached notes", svc.ReleaseNotes);
    }

    [Fact]
    public async Task Cooldown_AfterWindow_PollsAgainAndUpdatesSnapshot() {
        var settingsDb = new InMemorySettingsDb();
        var settings = new IndexedDbSettings(settingsDb);
        var now = DateTimeOffset.Parse("2026-06-23T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

        await settings.SetSettingsAsync(new Dictionary<string, string> {
            [UpdateService.LastUpdateCheckAtKey] = now.AddHours(-13).ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            [UpdateService.KnownLatestVersionKey] = "2.1.4",
            [UpdateService.KnownLatestReleaseNotesKey] = "",
        });
        var handler = new CountingHandler(_ => StubHttpMessageHandler.Json("""{"tag_name":"2.6.0","body":"fresh"}"""));
        var github = new GithubReleaseClient(new HttpClient(handler));
        var svc = new UpdateService(github, () => "2.1.4", settings: settings, now: () => now);

        await svc.CheckForUpdatesAsync();

        Assert.Equal(1, handler.Calls);
        Assert.Equal(UpdatePhase.Available, svc.Phase);
        Assert.Equal("2.6.0", svc.AvailableVersion);

        var stored = await settings.GetAllSettingsAsync();
        Assert.Equal("2.6.0", stored[UpdateService.KnownLatestVersionKey]);
        Assert.Equal(now.ToString("O", System.Globalization.CultureInfo.InvariantCulture), stored[UpdateService.LastUpdateCheckAtKey]);
    }

    [Fact]
    public async Task Cooldown_ForcedWithinWindow_BypassesCooldown_Polls() {
        var settingsDb = new InMemorySettingsDb();
        var settings = new IndexedDbSettings(settingsDb);
        var now = DateTimeOffset.Parse("2026-06-23T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

        await settings.SetSettingsAsync(new Dictionary<string, string> {
            [UpdateService.LastUpdateCheckAtKey] = now.AddHours(-1).ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            [UpdateService.KnownLatestVersionKey] = "2.5.0",
            [UpdateService.KnownLatestReleaseNotesKey] = "stale",
        });
        var handler = new CountingHandler(_ => StubHttpMessageHandler.Json("""{"tag_name":"2.7.0","body":"forced"}"""));
        var github = new GithubReleaseClient(new HttpClient(handler));
        var svc = new UpdateService(github, () => "2.1.4", settings: settings, now: () => now);

        await svc.CheckForUpdatesAsync(force: true);

        Assert.Equal(1, handler.Calls);
        Assert.Equal(UpdatePhase.Available, svc.Phase);
        Assert.Equal("2.7.0", svc.AvailableVersion);

        var stored = await settings.GetAllSettingsAsync();
        Assert.Equal("2.7.0", stored[UpdateService.KnownLatestVersionKey]);
    }

    [Fact]
    public async Task Cooldown_NoSettingsStore_AlwaysPolls() {

        var handler = new CountingHandler(_ => StubHttpMessageHandler.Json("""{"tag_name":"2.5.0","body":"n"}"""));
        var github = new GithubReleaseClient(new HttpClient(handler));
        var svc = new UpdateService(github, () => "2.1.4");

        await svc.CheckForUpdatesAsync();
        await svc.CheckForUpdatesAsync();

        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task RunSelfReplaceHandoff_LaunchesNewWithFlagsAndExits() {
        var exeDir = Path.Combine(Path.GetTempPath(), "egg-handoff-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(exeDir);
        var exePath = Path.Combine(exeDir, OperatingSystem.IsWindows() ? "EggLedger.exe" : "EggLedger");
        try {

            var tempPath = UpdateService.NewBinaryTempPath(exePath);
            await File.WriteAllBytesAsync(tempPath, new byte[8]);

            var runner = new RecordingRunner();
            var github = Github(_ => StubHttpMessageHandler.Json("{}"));
            var svc = new UpdateService(github, () => "2.1.4", () => exePath, runner);

            var exited = false;
            var ok = await svc.RunSelfReplaceHandoffAsync(
                () => {
                    exited = true;
                    return Task.CompletedTask;
                },
                handshakeTimeout: TimeSpan.FromMilliseconds(100),
                exitDelay: TimeSpan.Zero);

            Assert.True(ok);
            Assert.True(exited, "old instance should run the injected exit action");
            Assert.Equal(tempPath, runner.Exe);
            Assert.NotNull(runner.Args);
            Assert.Contains(runner.Args!, a => a.StartsWith("--replace-pid=", StringComparison.Ordinal));
            Assert.Contains(runner.Args!, a => a.StartsWith("--handshake-port=", StringComparison.Ordinal));
        } finally {
            Directory.Delete(exeDir, recursive: true);
        }
    }
}
