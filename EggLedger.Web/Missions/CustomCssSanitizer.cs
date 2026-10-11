using System.Globalization;
using System.Text.RegularExpressions;

namespace EggLedger.Web.Missions;

public static partial class CustomCssSanitizer {
    private const int MaxLength = 20_000;
    private const string ReplacementChar = "�";

    public static bool TryScope(string presetName, string? rawCss, out string? scopedCss, out string? error) {
        scopedCss = null;
        error = null;

        if (string.IsNullOrWhiteSpace(rawCss)) return true;

        if (rawCss.Length > MaxLength) {
            error = $"Custom CSS is too long (max {MaxLength} characters).";
            return false;
        }

        error = FindProblem(rawCss, DecodeCssEscapes(rawCss));
        if (error is not null) return false;

        scopedCss = $".{ScopeClassFor(presetName)} {{\n{rawCss}\n}}";
        return true;
    }

    private static string? FindProblem(string rawCss, string decodedCss) {
        if (ImportPattern().IsMatch(decodedCss)) return "@import is not allowed in custom CSS.";
        if (ExpressionPattern().IsMatch(decodedCss)) return "expression() is not allowed in custom CSS.";
        if (JavascriptUrlPattern().IsMatch(decodedCss)) return "javascript: URLs are not allowed in custom CSS.";
        if (StyleClosePattern().IsMatch(decodedCss)) return "</style> is not allowed in custom CSS.";
        if (decodedCss.Contains('<') || rawCss.Contains('<')) return "'<' is not allowed in custom CSS.";
        if (DisallowedAtRule(decodedCss) is { } name) return $"@{name} is not allowed in custom CSS; only @media and @container are.";
        if (DocumentSelectorPattern().IsMatch(decodedCss)) return ":root, html, and body selectors are not allowed in custom CSS.";
        if (!BracesBalanced(decodedCss)) return "Unbalanced braces in custom CSS.";
        if (HasForeignUrl(decodedCss)) return "url(...) is only allowed with data:image/ URIs in custom CSS.";
        return null;
    }

    private static string? DisallowedAtRule(string css) {
        return AtRulePattern().Matches(css)
            .Select(m => m.Groups[1].Value.ToLowerInvariant())
            .FirstOrDefault(n => n is not ("media" or "container"));
    }

    private static bool HasForeignUrl(string css) {
        return UrlPattern().Matches(css)
            .Any(m => !m.Groups[1].Value.Trim('\'', '"', ' ').StartsWith("data:image/", StringComparison.OrdinalIgnoreCase));
    }

    public static string ScopeClassFor(string presetName) {
        return "mission-card-custom-" + SlugFor(presetName);
    }

    private static bool BracesBalanced(string css) {
        var depth = 0;
        foreach (var ch in css) {
            if (ch == '{') {
                depth++;
            } else if (ch == '}' && --depth < 0) {
                return false;
            }
        }

        return depth == 0;
    }

    public static string SlugFor(string presetName) {
        string slug = SlugPattern().Replace(presetName, "-").Trim('-').ToLowerInvariant();
        return slug.Length == 0 ? "preset" : slug;
    }

    private static string DecodeCssEscapes(string css) {
        return CssEscapePattern().Replace(css, m => {
            string hex = m.Groups[1].Value;
            if (hex.Length > 0) {
                int codepoint = int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                return codepoint is 0 or >= 0xD800 and <= 0xDFFF or > 0x10FFFF ? ReplacementChar : char.ConvertFromUtf32(codepoint);
            }

            return m.Groups[2].Value;
        });
    }

    [GeneratedRegex(@"\\([0-9a-fA-F]{1,6})\s?|\\(.)")]
    private static partial Regex CssEscapePattern();

    [GeneratedRegex(@"@import", RegexOptions.IgnoreCase)]
    private static partial Regex ImportPattern();

    [GeneratedRegex(@"expression\s*\(", RegexOptions.IgnoreCase)]
    private static partial Regex ExpressionPattern();

    [GeneratedRegex(@"javascript\s*:", RegexOptions.IgnoreCase)]
    private static partial Regex JavascriptUrlPattern();

    [GeneratedRegex(@"</style", RegexOptions.IgnoreCase)]
    private static partial Regex StyleClosePattern();

    [GeneratedRegex(@"url\(\s*([^)]*)\)", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();

    [GeneratedRegex(@"@([a-zA-Z-]+)")]
    private static partial Regex AtRulePattern();

    [GeneratedRegex(@"(?:^|[\s,>+~{}])(?::root|html|body)(?=[\s,.:#\[>+~{]|$)", RegexOptions.IgnoreCase)]
    private static partial Regex DocumentSelectorPattern();

    [GeneratedRegex(@"[^a-zA-Z0-9]+")]
    private static partial Regex SlugPattern();
}
