namespace EggLedger.Desktop.Update;

public sealed class UpdateBootstrap(IProcessProbe probe, BinaryReplacement replacement) {
    public static readonly TimeSpan OldExitWaitTimeout = TimeSpan.FromSeconds(30);

    public const int RenameRetryAttemptsUnknownPid = 60;

    public const int RenameRetryAttemptsKnownPid = 10;

    public static readonly TimeSpan RenameRetryDelayUnknownPid = TimeSpan.FromMilliseconds(500);

    public static readonly TimeSpan RenameRetryDelayKnownPid = TimeSpan.FromMilliseconds(300);

    private readonly IProcessProbe _probe = probe;
    private readonly BinaryReplacement _replacement = replacement;

    public readonly record struct ReplaceArgs(int ReplacePid, string ReplacePath, string HandshakePort, string HandshakeToken);

    public static ReplaceArgs ParseArgs(string[] args) {
        var pid = 0;
        var path = "";
        var port = "";
        var token = "";

        for (var i = 0; i < args.Length; i++) {
            var (key, value) = SplitArg(args, ref i);
            switch (key) {
                case "--replace-pid":
                    _ = int.TryParse(value, out pid);
                    break;
                case "--replace-path":
                    path = value;
                    break;
                case "--handshake-port":
                    port = value;
                    break;
                case "--handshake-token":
                    token = value;
                    break;
            }
        }

        return new ReplaceArgs(pid, path, port, token);
    }

    private static (string Key, string Value) SplitArg(string[] args, ref int i) {
        var arg = args[i];
        var eq = arg.IndexOf('=', StringComparison.Ordinal);
        if (eq >= 0) {
            return (arg[..eq], arg[(eq + 1)..]);
        }

        if (arg.StartsWith("--", StringComparison.Ordinal) && i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)) {
            i++;
            return (arg, args[i]);
        }
        return (arg, "");
    }

    public bool RunStartup(string[] args, string? selfPath) {
        var self = selfPath ?? Environment.ProcessPath ?? "";
        var exeDir = string.IsNullOrEmpty(self) ? "" : (Path.GetDirectoryName(self) ?? "");

        if (!string.IsNullOrEmpty(exeDir)) {
            _replacement.CleanStaleBinaries(exeDir, self);
        }

        var parsed = ParseArgs(args);
        var (run, oldPid, oldPath) = BinaryNaming.DecideReplace(self, parsed.ReplacePid, parsed.ReplacePath);
        if (!run) {
            return false;
        }

        RunTakeover(self, oldPid, oldPath, parsed.HandshakePort, parsed.HandshakeToken);
        return true;
    }

    public void RunTakeover(string self, int oldPid, string oldPath, string handshakePort, string handshakeToken) {
        if (!string.IsNullOrEmpty(handshakePort) && !string.IsNullOrEmpty(handshakeToken)) {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var client = new HandshakeClient(http);
            client.PingOldReadyAsync(handshakePort, handshakeToken).GetAwaiter().GetResult();
        }

        if (oldPid > 0) {
            ProcessWait.WaitForExit(_probe, oldPid, OldExitWaitTimeout);
        }

        if (string.IsNullOrEmpty(self) || string.IsNullOrEmpty(oldPath)) {
            return;
        }

        var exeDir = Path.GetDirectoryName(oldPath) ?? "";
        var (release, ok) = _replacement.AcquireLock(Path.Combine(exeDir, BinaryReplacement.LockFileName));
        if (!ok) {
            return;
        }
        try {
            if (BinaryReplacement.SameFile(self, oldPath)) {

                return;
            }

            var attempts = oldPid == 0 ? RenameRetryAttemptsUnknownPid : RenameRetryAttemptsKnownPid;
            var delay = oldPid == 0 ? RenameRetryDelayUnknownPid : RenameRetryDelayKnownPid;
            BinaryReplacement.RenameWithRetry(self, oldPath, attempts, delay);
        } finally {
            release?.Invoke();
        }
    }
}
