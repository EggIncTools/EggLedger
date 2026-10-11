namespace EggLedger.Desktop.Update;

public static class BinaryNaming {
    public const string NewBinarySuffix = "_new";

    private const string ExeExt = ".exe";

    public static bool IsNewBinaryName(string path) {
        var baseName = Path.GetFileName(path);
        if (baseName.EndsWith(ExeExt, StringComparison.Ordinal)) baseName = baseName[..^ExeExt.Length];
        return baseName.EndsWith(NewBinarySuffix, StringComparison.Ordinal);
    }

    public static string CanonicalPathFromNew(string path) {
        var dir = Path.GetDirectoryName(path) ?? "";
        var baseName = Path.GetFileName(path);
        var ext = "";
        if (baseName.EndsWith(ExeExt, StringComparison.Ordinal)) {
            ext = ExeExt;
            baseName = baseName[..^ExeExt.Length];
        }
        if (baseName.EndsWith(NewBinarySuffix, StringComparison.Ordinal)) baseName = baseName[..^NewBinarySuffix.Length];
        return Path.Combine(dir, baseName + ext);
    }

    public static (bool Run, int OldPid, string OldPath) DecideReplace(
        string self, int replacePid, string replacePath) {
        var hasFlags = replacePid != 0 && !string.IsNullOrEmpty(replacePath);
        var isNew = !string.IsNullOrEmpty(self) && IsNewBinaryName(self);
        if (!isNew && !hasFlags) return (false, 0, "");
        var oldPath = replacePath;
        if (string.IsNullOrEmpty(oldPath)) oldPath = CanonicalPathFromNew(self);
        return (true, replacePid, oldPath);
    }
}
