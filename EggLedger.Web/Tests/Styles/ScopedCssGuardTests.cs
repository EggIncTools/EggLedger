using System.Text.RegularExpressions;
using EggIdentity.Styles;

namespace EggLedger.Web.Tests.Styles;

public partial class ScopedCssGuardTests {
    [Fact]
    public void No_apply_in_scoped_files() {
        var offenders = StylePaths.ScopedCssFiles()
            .Where(f => File.ReadAllText(f).Contains("@apply", StringComparison.Ordinal))
            .ToList();
        Assert.Empty(offenders);
    }

    [Fact]
    public void No_shared_class_redefined_in_scoped_files() {
        var shared = ComponentClasses.All.Keys.ToHashSet(StringComparer.Ordinal);
        var offenders = new List<string>();
        foreach (var file in StylePaths.ScopedCssFiles()) {
            foreach (var selector in Selectors(File.ReadAllText(file))) {
                foreach (Match m in ClassToken().Matches(selector)) {
                    if (shared.Contains(m.Groups[1].Value)) {
                        offenders.Add($"{Path.GetFileName(file)}: {selector.Trim()}");
                    }
                }
            }
        }
        Assert.Empty(offenders);
    }

    [Fact]
    public void No_unanchored_deep() {
        var offenders = StylePaths.ScopedCssFiles()
            .SelectMany(f => Selectors(File.ReadAllText(f)).Select(s => (f, s: s.Trim())))
            .Where(x => x.s.StartsWith("::deep", StringComparison.Ordinal))
            .Select(x => $"{Path.GetFileName(x.f)}: {x.s}")
            .ToList();
        Assert.Empty(offenders);
    }

    private static IEnumerable<string> Selectors(string css) {
        var stripped = Comment().Replace(css, "");
        foreach (Match m in RulePrelude().Matches(stripped)) {
            var prelude = m.Groups[1].Value.Trim();
            if (prelude.StartsWith('@') || prelude.EndsWith('%') || prelude is "from" or "to") {
                continue;
            }
            foreach (var part in prelude.Split(',')) {
                yield return part;
            }
        }
    }

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex Comment();

    [GeneratedRegex(@"(?:^|[{};])\s*([^{};]+?)\s*\{")]
    private static partial Regex RulePrelude();

    [GeneratedRegex(@"\.(-?[A-Za-z_][\w-]*)")]
    private static partial Regex ClassToken();
}
