namespace EggLedger.Web.Tests.Styles;

internal static class StylePaths {
    public static string RepoRoot { get; } = FindRepoRoot();

    public static string WebDir => Path.Combine(RepoRoot, "EggLedger.Web");

    public static string AppSheet => File.ReadAllText(Path.Combine(WebDir, "wwwroot", "app.css"));

    public static string SharedSheet => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "SharedStyles", "shared.css"));

    public static IEnumerable<string> ScopedCssFiles() =>
        new[] { "EggLedger.Web", "EggLedger.Web.Server", "EggLedger.Desktop" }
            .Select(p => Path.Combine(RepoRoot, p))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.razor.css", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    private static string FindRepoRoot() {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EggLedger.slnx"))) {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("EggLedger.slnx not found above test output directory.");
    }
}
