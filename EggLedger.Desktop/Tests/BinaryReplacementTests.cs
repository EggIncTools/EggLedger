using EggLedger.Desktop.Update;

namespace EggLedger.Desktop.Tests;

public sealed class BinaryReplacementTests : IDisposable {
    private readonly string _dir;

    public BinaryReplacementTests() {
        _dir = Path.Combine(Path.GetTempPath(), "egg-replace-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() {
        try {
            Directory.Delete(_dir, recursive: true);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException) {
        }
    }

    private sealed class FakeProbe(HashSet<int> alive) : IProcessProbe {
        public bool Exists(int pid) => alive.Contains(pid);
    }

    [Fact]
    public void RenameWithRetry_MovesFileContents() {
        var src = Path.Combine(_dir, "src.bin");
        var dst = Path.Combine(_dir, "dst.bin");
        File.WriteAllText(src, "new");

        Assert.True(BinaryReplacement.RenameWithRetry(src, dst, 3, TimeSpan.FromMilliseconds(10)));
        Assert.False(File.Exists(src));
        Assert.Equal("new", File.ReadAllText(dst));
    }

    [Fact]
    public void RenameWithRetry_OverwritesExistingDestination() {
        var src = Path.Combine(_dir, "src.bin");
        var dst = Path.Combine(_dir, "dst.bin");
        File.WriteAllText(src, "fresh");
        File.WriteAllText(dst, "stale");

        Assert.True(BinaryReplacement.RenameWithRetry(src, dst, 3, TimeSpan.FromMilliseconds(10)));
        Assert.Equal("fresh", File.ReadAllText(dst));
    }

    [Fact]
    public void AcquireLock_SecondAcquireFailsWhileHeldThenSucceedsAfterRelease() {

        var repl = new BinaryReplacement(new ProcessProbe());
        var lockPath = Path.Combine(_dir, BinaryReplacement.LockFileName);

        var (release, ok) = repl.AcquireLock(lockPath);
        Assert.True(ok);
        Assert.NotNull(release);

        var (_, ok2) = repl.AcquireLock(lockPath);
        Assert.False(ok2);

        release!();
        Assert.False(File.Exists(lockPath));

        var (release2, ok3) = repl.AcquireLock(lockPath);
        Assert.True(ok3);
        release2!();
    }

    [Fact]
    public void AcquireLock_ReclaimsStaleLockFromDeadOwner() {
        var lockPath = Path.Combine(_dir, BinaryReplacement.LockFileName);

        File.WriteAllText(lockPath, "424242");
        var repl = new BinaryReplacement(new FakeProbe([]));

        var (release, ok) = repl.AcquireLock(lockPath);
        Assert.True(ok);
        release!();
    }

    [Fact]
    public void CleanStaleBinaries_RemovesNewBinariesButNotSelf() {
        var self = Path.Combine(_dir, "EggLedger_new.exe");
        var otherNew = Path.Combine(_dir, "EggLedgerOther_new.exe");
        var canonical = Path.Combine(_dir, "EggLedger.exe");
        File.WriteAllText(self, "self");
        File.WriteAllText(otherNew, "stale");
        File.WriteAllText(canonical, "keep");

        var repl = new BinaryReplacement(new FakeProbe([]));
        repl.CleanStaleBinaries(_dir, self);

        Assert.True(File.Exists(self), "must not delete our own running image");
        Assert.False(File.Exists(otherNew), "stale _new binary should be removed");
        Assert.True(File.Exists(canonical), "canonical binary kept");
    }

    [Fact]
    public void CleanStaleBinaries_RemovesStaleLockWhenOwnerDead() {
        var self = Path.Combine(_dir, "EggLedger.exe");
        File.WriteAllText(self, "self");
        var lockPath = Path.Combine(_dir, BinaryReplacement.LockFileName);
        File.WriteAllText(lockPath, "424242");

        var repl = new BinaryReplacement(new FakeProbe([]));
        repl.CleanStaleBinaries(_dir, self);

        Assert.False(File.Exists(lockPath));
    }

    [Fact]
    public void CleanStaleBinaries_KeepsLockWhenOwnerAlive() {
        var self = Path.Combine(_dir, "EggLedger.exe");
        File.WriteAllText(self, "self");
        var lockPath = Path.Combine(_dir, BinaryReplacement.LockFileName);
        File.WriteAllText(lockPath, "777");

        var repl = new BinaryReplacement(new FakeProbe([777]));
        repl.CleanStaleBinaries(_dir, self);

        Assert.True(File.Exists(lockPath));
    }

    [Fact]
    public void ProcessProbe_CurrentProcessExists_BogusDoesNot() {
        var probe = new ProcessProbe();
        Assert.True(probe.Exists(Environment.ProcessId));

        Assert.False(probe.Exists(int.MaxValue - 1));
        Assert.False(probe.Exists(0));
        Assert.False(probe.Exists(-5));
    }

    [Fact]
    public void ProcessWait_ReturnsTrueImmediatelyForDeadPid() {
        var probe = new FakeProbe([]);
        Assert.True(ProcessWait.WaitForExit(probe, 4242, TimeSpan.Zero));
    }

    [Fact]
    public void ProcessWait_ReturnsFalseWhenAliveAndTimesOut() {
        var probe = new FakeProbe([4242]);
        Assert.False(ProcessWait.WaitForExit(probe, 4242, TimeSpan.FromMilliseconds(60)));
    }

    [Fact]
    public void SameFile_TrueForSamePathFalseForDifferent() {
        var a = Path.Combine(_dir, "a.bin");
        var b = Path.Combine(_dir, "b.bin");
        File.WriteAllText(a, "x");
        File.WriteAllText(b, "y");
        Assert.True(BinaryReplacement.SameFile(a, a));
        Assert.False(BinaryReplacement.SameFile(a, b));
    }
}
