using System.Diagnostics;

namespace EggLedger.Desktop.Update;

public interface IProcessProbe {
    bool Exists(int pid);
}

public sealed class ProcessProbe : IProcessProbe {
    public bool Exists(int pid) {
        if (pid <= 0) {
            return false;
        }
        try {
            using var proc = Process.GetProcessById(pid);
            return !proc.HasExited;
        } catch (ArgumentException) {

            return false;
        } catch (InvalidOperationException) {
            return false;
        }
    }
}

public static class ProcessWait {
    public static bool WaitForExit(IProcessProbe probe, int pid, TimeSpan timeout, TimeProvider? time = null) {
        var clock = time ?? TimeProvider.System;
        var start = clock.GetTimestamp();
        while (true) {
            if (!probe.Exists(pid)) {
                return true;
            }
            if (clock.GetElapsedTime(start) >= timeout) {
                return false;
            }
            Thread.Sleep(50);
        }
    }
}
