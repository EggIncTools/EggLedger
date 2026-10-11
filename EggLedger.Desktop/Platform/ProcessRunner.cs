using System.Diagnostics;

namespace EggLedger.Desktop.Platform;

public sealed class ProcessRunner : IProcessRunner {
    public Task RunAsync(string exe, IReadOnlyList<string> args) {
        var psi = new ProcessStartInfo {
            FileName = exe,
            UseShellExecute = false,
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        Process.Start(psi);
        return Task.CompletedTask;
    }
}
