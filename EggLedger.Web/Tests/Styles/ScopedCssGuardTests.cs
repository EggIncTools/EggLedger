using System.Text.RegularExpressions;
using EggIdentity.Styles;
using EggIdentity.Styles.Css;

namespace EggLedger.Web.Tests.Styles;

public partial class ScopedCssGuardTests {
    private static IEnumerable<(string File, CssSheet Sheet)> Sheets() =>
        StylePaths.ScopedCssFiles().Select(f => (Path.GetFileName(f), CssSheet.Load(f)));

    [Fact]
    public void No_engine_statements_in_scoped_files() {
        var offenders = Sheets()
            .SelectMany(x => x.Sheet.Statements.Select(s => $"{x.File}: {s}"))
            .ToList();
        Assert.Empty(offenders);
    }

    [Fact]
    public void No_shared_class_redefined_in_scoped_files() {
        var shared = ComponentClasses.All.Keys.ToHashSet(StringComparer.Ordinal);
        var offenders = Sheets()
            .SelectMany(x => StylePaths.SelectorParts(x.Sheet)
                .Where(s => ClassToken().Matches(s).Any(m => shared.Contains(m.Groups[1].Value)))
                .Select(s => $"{x.File}: {s}"))
            .ToList();
        Assert.Empty(offenders);
    }

    [Fact]
    public void No_unanchored_deep() {
        var offenders = Sheets()
            .SelectMany(x => StylePaths.SelectorParts(x.Sheet)
                .Where(s => s.StartsWith("::deep", StringComparison.Ordinal))
                .Select(s => $"{x.File}: {s}"))
            .ToList();
        Assert.Empty(offenders);
    }

    [Fact]
    public void No_var_fallbacks_in_scoped_files() {
        var offenders = Sheets()
            .SelectMany(x => x.Sheet.Rules.SelectMany(r => r.Declarations)
                .Where(d => VarFallback().IsMatch(d.Value))
                .Select(d => $"{x.File}: {d.Property}: {d.Value}"))
            .ToList();
        Assert.Empty(offenders);
    }

    [GeneratedRegex(@"\.(-?[A-Za-z_][\w-]*)")]
    private static partial Regex ClassToken();

    [GeneratedRegex(@"var\(--[\w-]+\s*,")]
    private static partial Regex VarFallback();
}
