using System.Text.RegularExpressions;

namespace EggLedger.Web.State;

public sealed partial class ScreenshotSafetyState {
    public bool Enabled {
        get;
        set {
            if (field == value) {
                return;
            }
            field = value;
            Changed?.Invoke();
        }
    }

    public event Action? Changed;

    public string Mask(string? text) =>
        string.IsNullOrEmpty(text) || !Enabled ? text ?? "" : EidRegex().Replace(text, "EI[eid-bar]");

    [GeneratedRegex(@"EI\d{16}")]
    private static partial Regex EidRegex();
}
