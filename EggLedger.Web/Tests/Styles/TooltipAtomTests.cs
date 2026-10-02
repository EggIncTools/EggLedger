using System.Text.RegularExpressions;

namespace EggLedger.Web.Tests.Styles;

public partial class TooltipAtomTests {
    [Fact]
    public void Every_class_the_tooltip_script_assigns_resolves() {
        var script = File.ReadAllText(Path.Combine(StylePaths.WebDir, "wwwroot", "js", "reportChartTooltip.js"));
        var defined = ClassResolutionTests.DefinedClasses(StylePaths.SharedSheet)
            .Concat(ClassResolutionTests.DefinedClasses(StylePaths.AppSheet))
            .ToHashSet(StringComparer.Ordinal);
        var unresolved = AssignedClassLiteral().Matches(script)
            .SelectMany(m => m.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(c => !defined.Contains(c) && !ClassResolutionTests.IsDynamic(c))
            .Distinct()
            .ToList();
        Assert.Empty(unresolved);
    }

    [GeneratedRegex(@"className\s*\+?=\s*""([^""]*)""")]
    private static partial Regex AssignedClassLiteral();
}
