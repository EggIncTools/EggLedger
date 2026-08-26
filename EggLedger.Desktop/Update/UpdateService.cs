using System.Globalization;
using System.Runtime.InteropServices;
using EggLedger.Desktop.Platform;
using EggLedger.Domain.Util;
using EggLedger.Web.Data;
using EggLedger.Web.Platform;

namespace EggLedger.Desktop.Update;

public sealed class UpdateService : IUpdateStatusProvider {
    public static readonly TimeSpan OldExitDelay = TimeSpan.FromSeconds(5);

    public static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromHours(12);

    public static readonly TimeSpan DefaultHandshakeTimeout = TimeSpan.FromSeconds(90);

    public const string LastUpdateCheckAtKey = "last_update_check_at";

    public const string KnownLatestVersionKey = "known_latest_version";

    public const string KnownLatestReleaseNotesKey = "known_latest_release_notes";

    private readonly GithubReleaseClient _github;
    private readonly IProcessRunner _processRunner;
    private readonly Func<string> _runningVersion;
    private readonly Func<string?> _exePath;
    private readonly Func<Task> _exitAction;
    private readonly TimeSpan _handshakeTimeout;
    private readonly TimeSpan _exitDelay;
    private readonly IndexedDbSettings? _settings;
    private readonly Func<DateTimeOffset> _now;

    public UpdateService(
        GithubReleaseClient github,
        Func<string> runningVersion,
        Func<string?>? exePath = null,
        IProcessRunner? processRunner = null,
        Func<Task>? exitAction = null,
        TimeSpan? handshakeTimeout = null,
        TimeSpan? exitDelay = null,
        IndexedDbSettings? settings = null,
        Func<DateTimeOffset>? now = null) {
        _github = github;
        _runningVersion = runningVersion;
        _exePath = exePath ?? (() => Environment.ProcessPath);
        _processRunner = processRunner ?? new ProcessRunner();
        _exitAction = exitAction ?? (() => Task.CompletedTask);
        _handshakeTimeout = handshakeTimeout ?? DefaultHandshakeTimeout;
        _exitDelay = exitDelay ?? OldExitDelay;
        _settings = settings;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public static IReadOnlyList<string> BuildReplaceArgs(int oldPid, string oldPath, string? handshakeAddr, string? handshakeToken) {
        List<string> args = [
            $"--replace-pid={oldPid.ToString(CultureInfo.InvariantCulture)}",
            $"--replace-path={oldPath}",
        ];
        if (!string.IsNullOrEmpty(handshakeAddr) && !string.IsNullOrEmpty(handshakeToken)) {
            args.Add($"--handshake-port={handshakeAddr}");
            args.Add($"--handshake-token={handshakeToken}");
        }
        return args;
    }

    public UpdatePhase Phase { get; private set; } = UpdatePhase.UpToDate;
    public string? AvailableVersion { get; private set; }
    public string? ReleaseNotes { get; private set; }
    public long DownloadedBytes { get; private set; }
    public long TotalBytes { get; private set; }
    public string? Message { get; private set; }

    public event Action? Changed;

    public async Task CheckForUpdatesAsync(bool force = false) {
        SetPhase(UpdatePhase.Checking);

        if (!SemVersion.TryParse(_runningVersion(), out var running) || running is null) {
            Fail("could not parse running version");
            return;
        }

        var snapshot = await ReadSnapshotAsync().ConfigureAwait(false);



        if (!force && !string.IsNullOrEmpty(snapshot.KnownTag)
            && SemVersion.TryParse(snapshot.KnownTag, out var knownVersion) && knownVersion is not null
            && knownVersion.GreaterThan(running)) {
            AvailableVersion = snapshot.KnownTag;
            ReleaseNotes = snapshot.KnownNotes;
            Message = null;
            SetPhase(UpdatePhase.Available);
            return;
        }



        if (!force && snapshot.LastCheckedAt is { } last && _now() - last < UpdateCheckInterval) {
            AvailableVersion = null;
            ReleaseNotes = null;
            Message = null;
            SetPhase(UpdatePhase.UpToDate);
            return;
        }

        var latest = await _github.GetLatestTagAsync().ConfigureAwait(false);
        if (latest is null) {
            Fail("could not fetch latest release");
            return;
        }

        var latestTag = latest.Value.Tag;
        var latestNotes = latest.Value.Body;
        if (!SemVersion.TryParse(latestTag, out var latestVersion) || latestVersion is null) {
            Fail($"could not parse latest version {latestTag}");
            return;
        }



        if (running.GreaterThan(latestVersion)) {
            var pre = await _github.GetLatestTagIncludingPreReleasesAsync().ConfigureAwait(false);
            if (pre is not null && SemVersion.TryParse(pre.Value.Tag, out var preVersion) && preVersion is not null) {
                latestTag = pre.Value.Tag;
                latestNotes = pre.Value.Body;
                latestVersion = preVersion;
            }
        }


        await WriteSnapshotAsync(latestTag, latestNotes).ConfigureAwait(false);

        if (running.LessThan(latestVersion)) {
            AvailableVersion = latestTag;
            ReleaseNotes = latestNotes;
            Message = null;
            SetPhase(UpdatePhase.Available);
        } else {
            AvailableVersion = null;
            ReleaseNotes = null;
            Message = null;
            SetPhase(UpdatePhase.UpToDate);
        }
    }

    private readonly record struct UpdateCheckSnapshot(
        DateTimeOffset? LastCheckedAt, string? KnownTag, string? KnownNotes);

    private async Task<UpdateCheckSnapshot> ReadSnapshotAsync() {
        if (_settings is null) {
            return default;
        }

        var all = await _settings.GetAllSettingsAsync().ConfigureAwait(false);
        DateTimeOffset? lastCheckedAt = null;
        if (all.TryGetValue(LastUpdateCheckAtKey, out var rawLast) && !string.IsNullOrEmpty(rawLast)
            && DateTimeOffset.TryParse(rawLast, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var parsed)) {
            lastCheckedAt = parsed;
        }

        all.TryGetValue(KnownLatestVersionKey, out var knownTag);
        all.TryGetValue(KnownLatestReleaseNotesKey, out var knownNotes);
        return new UpdateCheckSnapshot(lastCheckedAt, knownTag, knownNotes);
    }

    private async Task WriteSnapshotAsync(string latestTag, string latestNotes) {
        if (_settings is null) {
            return;
        }

        await _settings.SetSettingsAsync(new Dictionary<string, string> {
            [LastUpdateCheckAtKey] = _now().ToString("O", CultureInfo.InvariantCulture),
            [KnownLatestVersionKey] = latestTag,
            [KnownLatestReleaseNotesKey] = latestNotes,
        }).ConfigureAwait(false);
    }

    public async Task DownloadAndInstallAsync(string tag) {
        DownloadedBytes = 0;
        TotalBytes = 0;
        Message = null;
        SetPhase(UpdatePhase.Downloading);

        var exePath = _exePath();
        if (string.IsNullOrEmpty(exePath)) {
            Fail("could not resolve running executable path");
            return;
        }

        var assetUrl = await _github.GetUpdateAssetUrlAsync(tag).ConfigureAwait(false);
        if (assetUrl is null) {
            Fail($"no release asset for this platform in {tag}");
            return;
        }

        var tempPath = NewBinaryTempPath(exePath);
        BinaryReplacement.TryDelete(tempPath);



        var expectedSha = await _github.GetExpectedSha256Async(tag).ConfigureAwait(false);



        var assetName = GithubReleaseClient.ExpectedAssetName();
        if (ArchiveExtraction.IsArchive(assetName)) {
            var archivePath = Path.Combine(Path.GetTempPath(), assetName);
            BinaryReplacement.TryDelete(archivePath);
            try {
                await _github.DownloadAsync(assetUrl, archivePath, OnProgress).ConfigureAwait(false);
                if (!VerifySha256(archivePath, expectedSha)) {
                    BinaryReplacement.TryDelete(archivePath);
                    Fail("checksum mismatch: downloaded archive does not match the published SHA-256");
                    return;
                }
                ArchiveExtraction.Extract(archivePath, tempPath);
            } catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or InvalidOperationException) {
                BinaryReplacement.TryDelete(archivePath);
                BinaryReplacement.TryDelete(tempPath);
                Fail($"download failed: {ex.Message}");
                return;
            }
            BinaryReplacement.TryDelete(archivePath);
        } else {
            try {
                await _github.DownloadAsync(assetUrl, tempPath, OnProgress).ConfigureAwait(false);
                if (!VerifySha256(tempPath, expectedSha)) {
                    BinaryReplacement.TryDelete(tempPath);
                    Fail("checksum mismatch: downloaded binary does not match the published SHA-256");
                    return;
                }
            } catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException) {
                BinaryReplacement.TryDelete(tempPath);
                Fail($"download failed: {ex.Message}");
                return;
            }
        }

        AvailableVersion = tag;
        SetPhase(UpdatePhase.Ready);



        await RunSelfReplaceHandoffAsync(_exitAction, _handshakeTimeout, _exitDelay).ConfigureAwait(false);
    }

    public async Task<bool> RunSelfReplaceHandoffAsync(
        Func<Task> exitAction,
        TimeSpan? handshakeTimeout = null,
        TimeSpan? exitDelay = null) {
        var hsTimeout = handshakeTimeout ?? DefaultHandshakeTimeout;
        var oldExitDelay = exitDelay ?? OldExitDelay;
        var exePath = _exePath();
        if (string.IsNullOrEmpty(exePath)) {
            return false;
        }
        var tempPath = NewBinaryTempPath(exePath);
        if (!File.Exists(tempPath)) {
            return false;
        }

        var token = HandshakeToken.New();
        HandshakeListener? listener = null;
        try {
            listener = HandshakeListener.Start(token);
        } catch (Exception ex) when (ex is System.Net.HttpListenerException or System.Net.Sockets.SocketException) {

        }

        var args = BuildReplaceArgs(Environment.ProcessId, exePath, listener?.Address, token);
        try {
            await _processRunner.RunAsync(tempPath, args).ConfigureAwait(false);
        } catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException) {
            listener?.Dispose();

            Message = $"could not launch the updated instance: {ex.Message}";
            return false;
        }

        if (listener is not null) {
            await Task.WhenAny(listener.Served, Task.Delay(hsTimeout)).ConfigureAwait(false);
            listener.Dispose();
        }


        await Task.Delay(oldExitDelay).ConfigureAwait(false);
        await exitAction().ConfigureAwait(false);
        return true;
    }

    public static string NewBinaryTempPath(string exePath) {
        var dir = Path.GetDirectoryName(exePath) ?? "";
        var name = Path.GetFileName(exePath);
        var isExe = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
        if (isExe) {
            name = name[..^4];
        }
        name += BinaryNaming.NewBinarySuffix;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            name += ".exe";
        }
        return Path.Combine(dir, name);
    }

    private void OnProgress(long downloaded, long total) {
        DownloadedBytes = downloaded;
        TotalBytes = total;
        Changed?.Invoke();
    }

    private void SetPhase(UpdatePhase phase) {
        Phase = phase;
        Changed?.Invoke();
    }

    private void Fail(string message) {
        Message = message;
        SetPhase(UpdatePhase.Failed);
    }


    private static bool VerifySha256(string path, string? expected) {
        if (string.IsNullOrEmpty(expected)) {
            return true;
        }
        using var stream = File.OpenRead(path);
        var hash = System.Security.Cryptography.SHA256.HashData(stream);
        var hex = Convert.ToHexStringLower(hash);
        return string.Equals(hex, expected, StringComparison.Ordinal);
    }
}
