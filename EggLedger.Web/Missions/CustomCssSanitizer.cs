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

        string decodedCss = DecodeCssEscapes(rawCss);

        if (ImportPattern().IsMatch(decodedCss)) {
            error = "@import is not allowed in custom CSS.";
            return false;
        }

        if (ExpressionPattern().IsMatch(decodedCss)) {
            error = "expression() is not allowed in custom CSS.";
            return false;
        }

        if (JavascriptUrlPattern().IsMatch(decodedCss)) {
            error = "javascript: URLs are not allowed in custom CSS.";
            return false;
        }

        if (StyleClosePattern().IsMatch(decodedCss)) {
            error = "</style> is not allowed in custom CSS.";
            return false;
        }

        foreach (Match match in UrlPattern().Matches(decodedCss)) {
            string target = match.Groups[1].Value.Trim('\'', '"', ' ');
            if (!target.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)) {
                error = "url(...) is only allowed with data:image/ URIs in custom CSS.";
                return false;
            }
        }

        string className = "mission-card-custom-" + SlugFor(presetName);
        scopedCss = $".{className} {{\n{rawCss}\n}}";
        return true;
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

    [GeneratedRegex(@"[^a-zA-Z0-9]+")]
    private static partial Regex SlugPattern();
}
