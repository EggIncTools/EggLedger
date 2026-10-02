using System.Text;
using EggIdentity.Styles;
using EggIdentity.Styles.Theming;
using MonorailCss;
using MonorailCss.Parser.SourceCss;

namespace EggLedger.CssBuild;

public static class CssBuilder {
    public static string Build(string webProjectDir, Action<string>? log = null) {
        log ??= _ => { };
        var cssSourcePath = Path.Combine(webProjectDir, "Styles", "app.v4.css");
        if (!File.Exists(cssSourcePath)) {
            throw new InvalidOperationException($"CSS source file not found: {cssSourcePath}");
        }

        var rawSourceText = File.ReadAllText(cssSourcePath);
        if (CssBuildText.FindSemicolonInsideApplyBracket(rawSourceText) is { } violation) {
            throw new InvalidOperationException($"CSS build guard failed: {cssSourcePath}:{violation.Line} has a ';' inside a bracket value within an @apply body, near: {violation.Snippet}. Move that ';' outside the bracket value and rebuild.");
        }

        var registry = LedgerPalette.BuildRegistry();
        foreach (var (tokenName, _) in LedgerPalette.All) {
            if (!registry.IsKnown(tokenName) || registry.Canonicalize(tokenName) != tokenName) {
                throw new InvalidOperationException($"Palette token '{tokenName}' failed the theme token registry round-trip.");
            }
        }

        var themeHeaderIndex = rawSourceText.IndexOf("@theme {", StringComparison.Ordinal);
        if (themeHeaderIndex < 0) {
            throw new InvalidOperationException($"CSS source has no @theme block: {cssSourcePath}");
        }
        var themeBraceIndex = rawSourceText.IndexOf('{', themeHeaderIndex);
        var themeCloseIndex = FindMatchingBrace(rawSourceText, themeBraceIndex);
        var themeBody = rawSourceText.Substring(themeBraceIndex + 1, themeCloseIndex - themeBraceIndex - 1);
        if (themeBody.Contains("--color-", StringComparison.Ordinal)) {
            throw new InvalidOperationException($"CSS build drift guard failed: {cssSourcePath} still defines --color- tokens in its @theme block. Color tokens live in EggLedger.CssBuild/LedgerPalette.cs.");
        }

        var contrastColors = new Dictionary<string, ThemeColor>();
        foreach (var contrastName in LedgerPalette.ContrastBaseTokens.Concat(LedgerPalette.StatusTokens)) {
            var contrastValue = LedgerPalette.ComponentColors.First(c => c.Name == contrastName).Value;
            contrastColors[contrastName] = ThemeColor.FromHex(contrastValue)
                ?? throw new InvalidOperationException($"Palette token '{contrastName}' value '{contrastValue}' is not parseable hex for contrast validation.");
        }
        var contrastResult = ThemeContrast.Validate(contrastColors, ThemeChroma.None, LedgerPalette.StatusTokens);
        if (!contrastResult.Passes) {
            foreach (var contrastFailure in contrastResult.Failures) {
                log($"[contrast warning] {contrastFailure.Check}: {contrastFailure.A} vs {contrastFailure.B}, measured {contrastFailure.Measured:0.###}, required {contrastFailure.Required:0.###}");
            }
        }

        var newline = rawSourceText.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var colorDeclarations = new StringBuilder();
        foreach (var (colorName, colorValue) in LedgerPalette.All) {
            colorDeclarations.Append("  --color-").Append(colorName).Append(": ").Append(colorValue).Append(';').Append(newline);
        }
        var splicedText = rawSourceText
            .Remove(themeHeaderIndex, "@theme {".Length)
            .Insert(themeHeaderIndex, "@theme {" + newline + colorDeclarations);

        var processor = new CssSourceProcessor(message => log($"[monorail] {message}"));
        var sourceResult = processor.ProcessSource(splicedText, cssSourcePath, null);

        var mergedApplies = ComponentClasses.All.SetItems(sourceResult.Settings.Applies);
        var settings = sourceResult.Settings with { Applies = mergedApplies };

        var compiledCss = new CssFramework(settings).Process(mergedApplies.Keys);
        var strippedRawCss = CssBuildText.StripApplyDirectives(sourceResult.RawCss);
        var finalCss = CssBuildText.UnwrapLayersAndSpliceRaw(compiledCss, UnwrapLayerWrappers(strippedRawCss));

        return ":root {" + newline + colorDeclarations + "}" + newline + finalCss;
    }

    private static string UnwrapLayerWrappers(string css) {
        var result = new StringBuilder();
        var i = 0;
        while (true) {
            var atIndex = css.IndexOf("@layer", i, StringComparison.Ordinal);
            if (atIndex < 0) {
                result.Append(css, i, css.Length - i);
                return result.ToString();
            }
            result.Append(css, i, atIndex - i);
            var braceIndex = css.IndexOf('{', atIndex);
            if (braceIndex < 0) {
                result.Append(css, atIndex, css.Length - atIndex);
                return result.ToString();
            }
            var closeIndex = FindMatchingBrace(css, braceIndex);
            result.Append(css, braceIndex + 1, closeIndex - braceIndex - 1);
            i = closeIndex + 1;
        }
    }

    private static int FindMatchingBrace(string text, int openBraceIndex) {
        var depth = 0;
        for (var idx = openBraceIndex; idx < text.Length; idx++) {
            if (text[idx] == '{') {
                depth++;
            } else if (text[idx] == '}') {
                depth--;
                if (depth == 0) {
                    return idx;
                }
            }
        }
        throw new InvalidOperationException("Unbalanced braces in CSS while unwrapping @layer.");
    }
}
