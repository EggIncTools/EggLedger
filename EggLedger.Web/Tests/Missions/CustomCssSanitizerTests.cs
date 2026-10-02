using EggLedger.Web.Missions;

namespace EggLedger.Web.Tests.Missions;

public class CustomCssSanitizerTests {
    [Theory]
    [InlineData("color: red;")]
    [InlineData("background: url(data:image/png;base64,abcd);")]
    [InlineData("@media (min-width: 48rem) { span { color: red; } }")]
    [InlineData("@container (min-width: 20rem) { span { color: red; } }")]
    [InlineData("span.tbody { color: red; }")]
    [InlineData(".bodywork { color: red; }")]
    [InlineData("[data-x='html'] { color: red; }")]
    public void TryScope_AllowsCleanInput(string css) {
        bool ok = CustomCssSanitizer.TryScope("MyPreset", css, out string? scoped, out string? error);
        Assert.True(ok);
        Assert.Null(error);
        Assert.Contains(".mission-card-custom-", scoped);
        Assert.Contains(css, scoped);
    }

    [Fact]
    public void TryScope_NullOrBlank_ReturnsTrueWithNullScopedCss() {
        bool ok = CustomCssSanitizer.TryScope("MyPreset", null, out string? scoped, out string? error);
        Assert.True(ok);
        Assert.Null(error);
        Assert.Null(scoped);
    }

    [Theory]
    [InlineData("@import url(evil.css);")]
    [InlineData("width: expression(alert(1));")]
    [InlineData("background: url(javascript:alert(1));")]
    [InlineData("background: url(https://evil.com/x.png);")]
    [InlineData("content: '</style><script>alert(1)</script>';")]
    [InlineData(@"@\69mport ""https://evil.com/x"";")]
    [InlineData(@"background: \75rl(https://evil.com/x.png);")]
    [InlineData("@font-face { font-family: x; }")]
    [InlineData("@keyframes spin { to { transform: rotate(1turn); } }")]
    [InlineData("@layer base { div { color: red; } }")]
    [InlineData("@property --x { syntax: '<length>'; inherits: false; initial-value: 0; }")]
    [InlineData("@supports (display: grid) { div { color: red; } }")]
    [InlineData("@namespace svg url(data:image/png;base64,abcd);")]
    [InlineData("@charset \"utf-8\";")]
    [InlineData(@"@\66ont-face { font-family: x; }")]
    [InlineData(":root { --color-bg: red; }")]
    [InlineData("html { background: red; }")]
    [InlineData("body { display: none; }")]
    [InlineData("span, body { color: red; }")]
    [InlineData("HTML { background: red; }")]
    [InlineData(@"\3a root { --color-bg: red; }")]
    [InlineData("content: '<b>';")]
    [InlineData("content: '<!--';")]
    [InlineData(@"content: '\3c script';")]
    [InlineData("color: red; } span { color: blue;")]
    public void TryScope_RejectsDangerousPatterns(string css) {
        bool ok = CustomCssSanitizer.TryScope("MyPreset", css, out string? scoped, out string? error);
        Assert.False(ok);
        Assert.Null(scoped);
        Assert.NotNull(error);
    }

    [Fact]
    public void TryScope_AllowsLegitimateEscapeSequence() {
        string css = @"content: ""\2014"";";
        bool ok = CustomCssSanitizer.TryScope("MyPreset", css, out string? scoped, out string? error);
        Assert.True(ok);
        Assert.Null(error);
        Assert.Contains(css, scoped);
    }

    [Theory]
    [InlineData("My  Preset", "my-preset")]
    [InlineData("Weekend (v2)", "weekend-v2")]
    [InlineData("Classic", "classic")]
    public void SlugFor_CollapsesRunsOfSpecialChars_MatchingScopedClassName(string presetName, string expectedSlug) {
        string slug = CustomCssSanitizer.SlugFor(presetName);
        Assert.Equal(expectedSlug, slug);

        CustomCssSanitizer.TryScope(presetName, "color: red;", out string? scoped, out _);
        Assert.Contains($".mission-card-custom-{expectedSlug} ", scoped);
    }

    [Theory]
    [InlineData("color: red;")]
    [InlineData("@media (min-width: 48rem) { span { color: red; } }")]
    [InlineData(@"content: ""\2014"";")]
    public void TryScope_OutputNeverContainsMarkupOpeners(string css) {
        CustomCssSanitizer.TryScope("Weekend <v2>", css, out string? scoped, out _);
        Assert.NotNull(scoped);
        Assert.DoesNotContain("<", scoped);
        Assert.DoesNotContain("</style", scoped, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<!--", scoped);
    }

    [Fact]
    public void ScopedClassName_IsUserFacingApi() {
        CustomCssSanitizer.TryScope("Classic", "color: red;", out string? scoped, out _);
        Assert.StartsWith(".mission-card-custom-classic {", scoped);
        Assert.Equal("mission-card-custom-classic", CustomCssSanitizer.ScopeClassFor("Classic"));
    }

    [Fact]
    public void TryScope_RejectsOversizedInput() {
        string css = new('a', 20_001);
        bool ok = CustomCssSanitizer.TryScope("MyPreset", css, out string? scoped, out string? error);
        Assert.False(ok);
        Assert.Null(scoped);
        Assert.Contains("20", error);
    }
}
