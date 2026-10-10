using System.Text.RegularExpressions;
using EggIdentity.Styles.Css;

namespace EggLedger.Web.Tests.Styles;

public partial class ClassResolutionTests {

    private static readonly HashSet<string> DynamicAllowlist = [
        "tooltip-host",
        "tooltip-fixed",
        "tooltip-toggle",
        "tooltip-trigger",
        "btn-link",
        "prose-legal",
        "data-table",
        "overlay-drop",
        "overlay-target",
        "dismiss",
        "reload"
    ];

    private static readonly string[] DynamicPrefixes = [
        "mission-card-custom-",
        "text-duration-",
        "text-rarity-",
        "bg-r-",
        "timeline-dur-",
        "timeline-event-",
        "drop-label-",
        "drop-count-",
        "view-btn-",
        "flog-stage-",
        "picker-trigger-",
        "mov-size-",
        "about-"
    ];

    [Fact]
    public void Every_static_class_in_markup_resolves_to_a_rule() {
        var global = DefinedClasses(StylePaths.SharedSheet).Concat(DefinedClasses(StylePaths.AppSheet)).ToHashSet(StringComparer.Ordinal);
        var unresolved = new List<string>();
        foreach (var razor in RazorFiles()) {
            var scopedPath = razor + ".css";
            var scoped = File.Exists(scopedPath) ? DefinedClasses(CssSheet.Load(scopedPath)) : [];
            foreach (var cls in StaticClasses(File.ReadAllText(razor))) {
                if (global.Contains(cls) || scoped.Contains(cls) || IsDynamic(cls)) {
                    continue;
                }
                unresolved.Add($"{Path.GetFileName(razor)}: {cls}");
            }
        }
        Assert.True(unresolved.Count == 0, string.Join("\n", unresolved.Distinct()));
    }

    private static IEnumerable<string> RazorFiles() =>
        new[] { "EggLedger.Web", "EggLedger.Web.Server" }
            .Select(p => Path.Combine(StylePaths.RepoRoot, p))
            .SelectMany(d => Directory.EnumerateFiles(d, "*.razor", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}Tests{Path.DirectorySeparatorChar}"));

    internal static bool IsDynamic(string cls) =>
        DynamicAllowlist.Contains(cls) || DynamicPrefixes.Any(cls.StartsWith);

    internal static HashSet<string> DefinedClasses(CssSheet sheet) =>
        StylePaths.SelectorParts(sheet)
            .SelectMany(s => CssClass().Matches(s).Select(m => m.Groups[1].Value))
            .ToHashSet(StringComparer.Ordinal);

    private static IEnumerable<string> StaticClasses(string razor) {
        foreach (Match m in ClassAttr().Matches(razor)) {
            if (m.Groups[1].Value.Contains('@')) {
                continue;
            }
            foreach (var token in m.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)) {
                if (TokenShape().IsMatch(token)) {
                    yield return token;
                }
            }
        }
    }

    [GeneratedRegex(@"\.(-?[A-Za-z_][\w-]*)")]
    private static partial Regex CssClass();

    [GeneratedRegex(@"\sclass=""([^""]*)""")]
    private static partial Regex ClassAttr();

    [GeneratedRegex(@"^[a-z][a-z0-9_-]*$")]
    private static partial Regex TokenShape();
}
