using System.Runtime.InteropServices;

namespace EggLedger.Desktop.Platform;

public static class DesktopCommandBuilder {
    public static (string Exe, string[] Args) BuildOpenCommand(OSPlatform platform, string path) {
        if (platform == OSPlatform.Windows) {
            return ("explorer.exe", [path]);
        }
        if (platform == OSPlatform.OSX) {
            return ("open", [path]);
        }
        return ("xdg-open", [path]);
    }

    public static (string Exe, string[] Args) BuildOpenInFolderCommand(OSPlatform platform, string path) {
        if (platform == OSPlatform.Windows) {
            return ("explorer.exe", ["/select," + path]);
        }
        if (platform == OSPlatform.OSX) {
            return ("open", ["-R", path]);
        }
        var dir = Path.GetDirectoryName(path) ?? path;
        return ("xdg-open", [dir]);
    }

    public static (string Exe, string[] Args) BuildRestartCommand(string exePath)
        => (exePath, []);

    public static (string Exe, string[] Args)? BuildSetHiddenCommand(OSPlatform platform, string path, bool hidden)
        => platform != OSPlatform.OSX ? null : ("chflags", [hidden ? "hidden" : "nohidden", path]);
}
