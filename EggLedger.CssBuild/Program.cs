using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using EggIdentity.Styles;
using EggIdentity.Styles.Theming;
using EggLedger.CssBuild;
using MonorailCss;
using MonorailCss.Parser.SourceCss;

if (args.Length < 1) {
    Console.Error.WriteLine("Usage: EggLedger.CssBuild <EggLedger.Web project directory>");
    return 1;
}

var webProjectDir = Path.GetFullPath(args[0]);
if (!Directory.Exists(webProjectDir)) {
    Console.Error.WriteLine($"Web project directory not found: {webProjectDir}");
    return 1;
}

var cssSourcePath = Path.Combine(webProjectDir, "Styles", "app.v4.css");
if (!File.Exists(cssSourcePath)) {
    Console.Error.WriteLine($"CSS source file not found: {cssSourcePath}");
    return 1;
}

var outputPath = Path.Combine(webProjectDir, "wwwroot", "styles.css");
var desktopHtmlPath = Path.GetFullPath(Path.Combine(webProjectDir, "..", "EggLedger.Desktop", "wwwroot", "desktop.html"));
var serverProjectDir = Path.GetFullPath(Path.Combine(webProjectDir, "..", "EggLedger.Web.Server"));

var contentFiles = new List<string>();
contentFiles.AddRange(Directory.EnumerateFiles(webProjectDir, "*.razor", SearchOption.AllDirectories));
if (File.Exists(desktopHtmlPath)) {
    contentFiles.Add(desktopHtmlPath);
}
if (Directory.Exists(serverProjectDir)) {
    contentFiles.AddRange(Directory.EnumerateFiles(serverProjectDir, "*.razor", SearchOption.AllDirectories));
}

var rawSourceText = File.ReadAllText(cssSourcePath);
var applyGuardViolation = FindSemicolonInsideApplyBracket(rawSourceText);
if (applyGuardViolation is { } violation) {
    Console.Error.WriteLine($"CSS build guard failed: {cssSourcePath}:{violation.Line} has a ';' inside a bracket value within an @apply body, near: {violation.Snippet}");
    Console.Error.WriteLine("Move that ';' outside the bracket value and rebuild.");
    Console.Error.WriteLine("Static text guard only; cannot detect rules already mangled upstream by the parser.");
    return 1;
}

var registry = LedgerPalette.BuildRegistry();
foreach (var (tokenName, _) in LedgerPalette.ComponentColors.Concat(LedgerPalette.AppColors)) {
    if (!registry.IsKnown(tokenName) || registry.Canonicalize(tokenName) != tokenName) {
        Console.Error.WriteLine($"Palette token '{tokenName}' failed the theme token registry round-trip.");
        return 1;
    }
}

var themeHeaderIndex = rawSourceText.IndexOf("@theme {", StringComparison.Ordinal);
if (themeHeaderIndex < 0) {
    Console.Error.WriteLine($"CSS source has no @theme block: {cssSourcePath}");
    return 1;
}
var themeBraceIndex = rawSourceText.IndexOf('{', themeHeaderIndex);
var themeCloseIndex = FindMatchingBrace(rawSourceText, themeBraceIndex);
var themeBody = rawSourceText.Substring(themeBraceIndex + 1, themeCloseIndex - themeBraceIndex - 1);
if (themeBody.Contains("--color-", StringComparison.Ordinal)) {
    Console.Error.WriteLine($"CSS build drift guard failed: {cssSourcePath} still defines --color- tokens in its @theme block.");
    Console.Error.WriteLine("Color tokens live in EggLedger.CssBuild/LedgerPalette.cs; remove them from the CSS file.");
    return 1;
}

var contrastColors = new Dictionary<string, ThemeColor>();
foreach (var contrastName in LedgerPalette.ContrastBaseTokens.Concat(LedgerPalette.StatusTokens)) {
    var contrastValue = LedgerPalette.ComponentColors.First(c => c.Name == contrastName).Value;
    if (ThemeColor.FromHex(contrastValue) is not { } themeColor) {
        Console.Error.WriteLine($"Palette token '{contrastName}' value '{contrastValue}' is not parseable hex for contrast validation.");
        return 1;
    }
    contrastColors[contrastName] = themeColor;
}
var contrastResult = ThemeContrast.Validate(contrastColors, ThemeChroma.None, LedgerPalette.StatusTokens);
if (!contrastResult.Passes) {
    foreach (var contrastFailure in contrastResult.Failures) {
        Console.WriteLine($"[contrast warning] {contrastFailure.Check}: {contrastFailure.A} vs {contrastFailure.B}, measured {contrastFailure.Measured:0.###}, required {contrastFailure.Required:0.###}");
    }
}

var newline = rawSourceText.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
var colorDeclarations = new StringBuilder();
foreach (var (colorName, colorValue) in LedgerPalette.ComponentColors.Concat(LedgerPalette.AppColors)) {
    colorDeclarations.Append("  --color-").Append(colorName).Append(": ").Append(colorValue).Append(';').Append(newline);
}
var splicedText = rawSourceText
    .Remove(themeHeaderIndex, "@theme {".Length)
    .Insert(themeHeaderIndex, "@theme {" + newline + colorDeclarations);

Console.WriteLine($"Scanning {contentFiles.Count} content files for utility/component class tokens...");
var candidates = ContentScanner.Scan(contentFiles);
candidates.UnionWith(ContentSafelist.Tokens);
Console.WriteLine($"Found {candidates.Count} distinct candidate tokens.");

var processor = new CssSourceProcessor(message => Console.WriteLine($"[monorail] {message}"));
var sourceResult = processor.ProcessSource(splicedText, cssSourcePath, null);

var mergedApplies = ComponentClasses.All.SetItems(sourceResult.Settings.Applies);
var settings = sourceResult.Settings with { Applies = mergedApplies };

var framework = new CssFramework(settings);
var compiledCss = framework.Process(candidates);
File.WriteAllText(Path.Combine(Path.GetTempPath(), "monorail-dump-ledger.css"), compiledCss);

var strippedRawCss = StripApplyDirectives(sourceResult.RawCss);

var finalCss = UnwrapLayersAndSpliceRaw(compiledCss, strippedRawCss);

Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
File.WriteAllText(outputPath, finalCss);

Console.WriteLine($"Wrote {finalCss.Length} chars to {outputPath}");
return 0;

static (int Line, string Snippet)? FindSemicolonInsideApplyBracket(string text) {
    var searchStart = 0;
    while (true) {
        var applyIndex = text.IndexOf("@apply", searchStart, StringComparison.Ordinal);
        if (applyIndex < 0) {
            return null;
        }
        var depth = 0;
        var pos = applyIndex + "@apply".Length;
        while (pos < text.Length) {
            var c = text[pos];
            if (c == '[') {
                depth++;
            } else if (c == ']') {
                depth = Math.Max(0, depth - 1);
            } else if (c == ';') {
                if (depth > 0) {
                    var line = 1;
                    for (var j = 0; j < pos; j++) {
                        if (text[j] == '\n') {
                            line++;
                        }
                    }
                    var snippetStart = Math.Max(applyIndex, pos - 40);
                    var snippet = text.Substring(snippetStart, pos - snippetStart + 1);
                    return (line, snippet);
                }
                break;
            }
            pos++;
        }
        searchStart = applyIndex + "@apply".Length;
    }
}

static string UnwrapLayersAndSpliceRaw(string compiledCss, string rawCss) {
    var withoutLayerStatement = Regex.Replace(compiledCss, @"^@layer\s+[^;{]+;\s*\n?", "", RegexOptions.Multiline);

    var layers = new List<(string Name, string Content)>();
    var remainder = new StringBuilder();
    var i = 0;
    while (true) {
        var atIndex = withoutLayerStatement.IndexOf("@layer", i, StringComparison.Ordinal);
        if (atIndex < 0) {
            remainder.Append(withoutLayerStatement, i, withoutLayerStatement.Length - i);
            break;
        }
        remainder.Append(withoutLayerStatement, i, atIndex - i);
        var braceIndex = withoutLayerStatement.IndexOf('{', atIndex);
        var name = withoutLayerStatement.Substring(atIndex + "@layer".Length, braceIndex - atIndex - "@layer".Length).Trim();
        var closeIndex = FindMatchingBrace(withoutLayerStatement, braceIndex);
        layers.Add((name, withoutLayerStatement.Substring(braceIndex + 1, closeIndex - braceIndex - 1)));
        i = closeIndex + 1;
    }

    var unwrappedRaw = UnwrapLayerWrappers(rawCss);

    var insertIndex = layers.FindIndex(l => l.Name == "components");
    insertIndex = insertIndex >= 0 ? insertIndex + 1 : layers.FindIndex(l => l.Name == "utilities");
    if (insertIndex < 0) {
        insertIndex = layers.Count;
    }

    var orderedContents = layers.Select(l => l.Content).ToList();
    orderedContents.Insert(insertIndex, unwrappedRaw);

    var result = new StringBuilder(remainder.ToString());
    foreach (var content in orderedContents) {
        result.Append(content).Append('\n');
    }
    return result.ToString();
}

static string UnwrapLayerWrappers(string css) {
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

static int FindMatchingBrace(string text, int openBraceIndex) {
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

static string StripApplyDirectives(string css) {
    var result = new StringBuilder(css.Length);
    var i = 0;
    while (true) {
        var applyIndex = css.IndexOf("@apply", i, StringComparison.Ordinal);
        if (applyIndex < 0) {
            result.Append(css, i, css.Length - i);
            return result.ToString();
        }
        result.Append(css, i, applyIndex - i);
        var depth = 0;
        var pos = applyIndex + "@apply".Length;
        var terminatorFound = false;
        while (pos < css.Length) {
            var c = css[pos];
            if (c == '[') {
                depth++;
            } else if (c == ']') {
                depth = Math.Max(0, depth - 1);
            } else if (c == ';' && depth == 0) {
                pos++;
                terminatorFound = true;
                break;
            }
            pos++;
        }
        if (!terminatorFound) {
            result.Append(css, applyIndex, css.Length - applyIndex);
            return result.ToString();
        }
        i = pos;
    }
}
