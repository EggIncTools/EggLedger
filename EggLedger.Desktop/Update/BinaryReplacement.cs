using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EggLedger.Desktop.Update;

public sealed class BinaryReplacement(IProcessProbe probe, ILogger<BinaryReplacement>? logger = null) {
    public const string LockFileName = ".egg-update.lock";

    private readonly IProcessProbe _probe = probe;
    private readonly ILogger<BinaryReplacement> _logger = logger ?? NullLogger<BinaryReplacement>.Instance;

    public static bool RenameWithRetry(string src, string dst, int attempts, TimeSpan delay, ILogger? logger = null) {
        for (var i = 0; i < attempts; i++) {
            try {

                File.Move(src, dst, overwrite: true);
                return true;
            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
                logger?.LogDebug(ex, "update: rename attempt {Attempt} failed for {Destination}", i + 1, dst);
                Thread.Sleep(delay);
            }
        }
        return false;
    }

    public (Action? Release, bool Acquired) AcquireLock(string lockPath) {
        FileStream? TryCreate() {
            try {
                return new FileStream(lockPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            } catch (IOException) {
                return null;
            } catch (UnauthorizedAccessException) {
                return null;
            }
        }

        var f = TryCreate();
        if (f is null) {

            if (TryReadPid(lockPath, out var pid) && _probe.Exists(pid)) {
                return (null, false);
            }

            TryDelete(lockPath, _logger);
            f = TryCreate();
            if (f is null) {
                return (null, false);
            }
        }

        using (f) {
            var pidBytes = System.Text.Encoding.UTF8.GetBytes(
                Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
            f.Write(pidBytes, 0, pidBytes.Length);
        }

        var released = false;
        void Release() {
            if (released) {
                return;
            }
            released = true;
            TryDelete(lockPath, _logger);
        }
        return (Release, true);
    }

    public void CleanStaleBinaries(string exeDir, string selfPath) {
        string[] patterns = [$"EggLedger*{BinaryNaming.NewBinarySuffix}", $"EggLedger*{BinaryNaming.NewBinarySuffix}.exe"];
        foreach (var pattern in patterns) {
            string[] matches;
            try {
                matches = Directory.GetFiles(exeDir, pattern);
            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException) {
                _logger.LogDebug(ex, "update: listing stale binaries failed for pattern {Pattern}", pattern);
                continue;
            }
            foreach (var match in matches) {
                if (SameFile(match, selfPath, _logger)) {
                    continue;
                }
                TryDelete(match, _logger);
            }
        }

        var lockPath = Path.Combine(exeDir, LockFileName);
        if (TryReadPid(lockPath, out var pid)) {
            if (!_probe.Exists(pid)) {
                TryDelete(lockPath, _logger);
            }
        } else if (File.Exists(lockPath)) {

            TryDelete(lockPath, _logger);
        }
    }

    public static bool SameFile(string a, string b, ILogger? logger = null) {
        try {
            var fa = new FileInfo(a);
            var fb = new FileInfo(b);
            if (fa.Exists && fb.Exists) {
                var comparison = OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;
                return string.Equals(fa.FullName, fb.FullName, comparison);
            }
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) {
            logger?.LogDebug(ex, "update: file identity check failed, falling back to path comparison");
        }

        try {
            var ra = Path.GetFullPath(a);
            var rb = Path.GetFullPath(b);
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            return string.Equals(ra, rb, comparison);
        } catch (Exception ex) when (ex is ArgumentException or IOException) {
            logger?.LogDebug(ex, "update: path comparison failed");
            return false;
        }
    }

    private static bool TryReadPid(string lockPath, out int pid) {
        pid = 0;
        try {
            var data = File.ReadAllText(lockPath).Trim();
            return int.TryParse(data, NumberStyles.Integer, CultureInfo.InvariantCulture, out pid);
        } catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or IOException or UnauthorizedAccessException) {
            return false;
        }
    }

    internal static void TryDelete(string path, ILogger? logger = null) {
        try {
            File.Delete(path);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            logger?.LogDebug(ex, "update: delete failed for {Path}", path);
        }
    }
}
