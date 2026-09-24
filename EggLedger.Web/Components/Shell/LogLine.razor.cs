using System.Text.RegularExpressions;
using EggLedger.Web.State;
using Microsoft.AspNetCore.Components;

namespace EggLedger.Web.Components.Shell;

public enum LogRunKind {
    Text,
    Image,
    EidBar,
}

public sealed record LogRun(LogRunKind Kind, string Text, string? Color = null);

public partial class LogLine {
    private const string EidBarToken = "[eid-bar]";

    private IReadOnlyList<LogRun> _runs = [];

    [Parameter] public string Text { get; set; } = "";

    [Parameter] public bool IsError { get; set; }

    [Parameter] public bool Masked { get; set; }

    [Parameter] public string DefaultClass { get; set; } = "text-gray-400";

    [Inject] private ScreenshotSafetyState ScreenshotSafety { get; set; } = default!;

    protected override void OnParametersSet() {
        _runs = Parse(Masked ? ScreenshotSafety.Mask(Text) : Text);
    }

    public static IReadOnlyList<LogRun> Parse(string text) {
        List<LogRun> runs = [];
        var i = 0;
        while (i < text.Length) {
            if (text.AsSpan(i).StartsWith(EidBarToken, StringComparison.Ordinal)) {
                runs.Add(new(LogRunKind.EidBar, ""));
                i += EidBarToken.Length;
                continue;
            }

            var image = ImageToken().Match(text, i);
            if (image.Success) {
                runs.Add(new(LogRunKind.Image, image.Groups[1].Value));
                i += image.Length;
                continue;
            }

            var color = ColorToken().Match(text, i);
            if (color.Success) {
                AddColored(runs, color.Groups[2].Value, "#" + color.Groups[1].Value);
                i += color.Length;
                continue;
            }

            var next = NextToken().Match(text, i + 1);
            var end = next.Success ? next.Index : text.Length;
            AddPlain(runs, text[i..end]);
            i = end;
        }
        return runs;
    }

    private static void AddColored(List<LogRun> runs, string body, string color) {
        var parts = body.Split(EidBarToken);
        for (var p = 0; p < parts.Length; p++) {
            if (parts[p].Length > 0) {
                runs.Add(new(LogRunKind.Text, parts[p], color));
            }
            if (p < parts.Length - 1) {
                runs.Add(new(LogRunKind.EidBar, ""));
            }
        }
    }

    private static void AddPlain(List<LogRun> runs, string text) {
        if (runs is [.., { Kind: LogRunKind.Text, Color: null } last]) {
            runs[^1] = last with { Text = last.Text + text };
            return;
        }
        runs.Add(new(LogRunKind.Text, text));
    }

    [GeneratedRegex(@"\G\[img:([\w.-]+)\]")]
    private static partial Regex ImageToken();

    [GeneratedRegex(@"\G&([0-9a-fA-F]{6})<([^>]*)>")]
    private static partial Regex ColorToken();

    [GeneratedRegex(@"\[eid-bar\]|\[img:|&[0-9a-fA-F]{6}<")]
    private static partial Regex NextToken();
}
