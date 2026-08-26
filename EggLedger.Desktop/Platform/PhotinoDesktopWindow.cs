using Photino.NET;

namespace EggLedger.Desktop.Platform;

public sealed class PhotinoDesktopWindow : IDesktopWindow {
    public PhotinoDesktopWindow() {
    }

    public PhotinoDesktopWindow(PhotinoWindow window) => Window = window;

    public void Attach(PhotinoWindow window) => Window = window;

    public (int Width, int Height) GetSize() {
        var w = Window;
        return (w.Width, w.Height);
    }

    public string? ShowSaveFileDialog(string defaultName) {
        string ext = Path.GetExtension(defaultName).TrimStart('.');
        (string Name, string[] Extensions)[] filters = [
            (FilterName(ext), [ext]),
        ];
        var chosen = Window.ShowSaveFile("Save As", defaultName, filters);
        return string.IsNullOrEmpty(chosen) ? null : chosen;
    }

    private static string FilterName(string ext) => ext.ToUpperInvariant() switch {
        "CSV" => "CSV files",
        "XLSX" => "Excel files",
        "JSON" => "JSON files",
        _ => $"{ext.ToUpperInvariant()} files",
    };

    public void ExitProcess() => Environment.Exit(0);

    public string? ShowOpenFolderDialog() {
        var chosen = Window.ShowOpenFolder("Choose Folder", "", multiSelect: false);
        return chosen is { Length: > 0 } && !string.IsNullOrEmpty(chosen[0]) ? chosen[0] : null;
    }

    public void SetSize(int width, int height) => Window.SetSize(width, height);

    public void SetFullScreen(bool fullScreen) => Window.SetFullScreen(fullScreen);

    private PhotinoWindow Window { get => field ?? throw new InvalidOperationException("Photino window not attached yet; call Attach after building the host."); set; }
}
