using EggIdentity.Styles.Theming;

namespace EggLedger.CssBuild;

public static class LedgerPalette {
    public static IReadOnlyList<(string Name, string Value)> ComponentColors { get; } = [
        ("bg", "#242629"),
        ("panel0", "#1c1d1f"),
        ("panel", "#33353a"),
        ("panel2", "#393b40"),
        ("fg", "#e5e7eb"),
        ("muted", "#99a1af"),
        ("accent", "#3b82f6"),
        ("accent2", "#2563eb"),
        ("ok", "#10b981"),
        ("err", "#b91c1c"),
        ("border", "#364153")
    ];

    public static IReadOnlyList<(string Name, string Value)> AppColors { get; } = [
        ("white", "#ffffff"),
        ("dark", "#393b40"),
        ("darkerthandark", "#33353a"),
        ("darker", "#242629"),
        ("darkerer", "#1c1d1f"),
        ("darkest", "#151617"),
        ("darkester", "#0e0f10"),
        ("dark_tab", "#323633"),
        ("darker_tab", "#262927"),
        ("dark_tab_hover", "#3a3d3a"),
        ("darker_tab_hover", "#2e312e"),
        ("dark_tab_border", "#111211"),
        ("blue-50", "#eff6ff"),
        ("blue-100", "#dbeafe"),
        ("blue-200", "#bfdbfe"),
        ("blue-300", "#93c5fd"),
        ("blue-400", "#60a5fa"),
        ("blue-500", "#3b82f6"),
        ("blue-600", "#2563eb"),
        ("blue-700", "#1d4ed8"),
        ("blue-800", "#1e40af"),
        ("blue-900", "#1e3a8a"),
        ("blue-950", "#172554"),
        ("green-50", "#ecfdf5"),
        ("green-100", "#d1fae5"),
        ("green-200", "#a7f3d0"),
        ("green-300", "#6ee7b7"),
        ("green-400", "#34d399"),
        ("green-500", "#10b981"),
        ("green-600", "#059669"),
        ("green-700", "#047857"),
        ("green-800", "#065f46"),
        ("green-900", "#064e3b"),
        ("green-950", "#022c22"),
        ("yellow-700", "rgb(251 191 36)"),
        ("red-700", "rgb(185 28 28)"),
        ("duration-0", "rgb(69, 159, 246)"),
        ("duration-1", "rgb(139, 93, 246)"),
        ("duration-2", "rgb(246, 168, 35)"),
        ("duration-3", "rgb(115, 128, 140)"),
        ("farm-home", "rgb(69, 159, 246)"),
        ("farm-virtue", "rgb(246, 168, 35)"),
        ("tutorial", "rgb(115, 128, 140)"),
        ("short", "rgb(69, 159, 246)"),
        ("standard", "rgb(139, 93, 246)"),
        ("extended", "rgb(246, 168, 35)"),
        ("rarity-0", "rgb(156 163 175)"),
        ("rarity-1", "#6ab6ff"),
        ("rarity-2", "#c03fe2"),
        ("rarity-3", "#eeab42"),
        ("rare", "#6ab6ff"),
        ("epic", "#c03fe2"),
        ("legendary", "#eeab42"),
        ("goldenstar", "rgb(255, 215, 0)"),
        ("shortdarker", "rgb(48, 111, 171)"),
        ("dubcap", "rgb(173, 10, 198)"),
        ("dubcapdarker", "rgb(120, 7, 138)"),
        ("selectedmission", "rgb(10, 173, 82)"),
        ("selectedmissiondarker", "rgb(7, 120, 56)"),
        ("buggedcap", "rgb(198, 10, 10)"),
        ("buggedcapdarker", "rgb(138, 7, 7)"),
        ("privacy_blue", "#276ec8"),
        ("data_loss_red", "#820808"),
        ("upgrade_green", "#1c802e"),
        ("upgrade_green_hover", "#155e22"),
        ("upgrade_green_border", "#155e22"),
        ("upgrade_green_hover_border", "#114f1c")
    ];

    public static IReadOnlyList<string> StatusTokens { get; } = ["accent", "ok", "err"];

    public static IReadOnlyList<string> ContrastBaseTokens { get; } = ["bg", "panel0", "panel", "panel2", "fg", "muted", "border"];

    public static ThemeTokenRegistry BuildRegistry() {
        var registry = new ThemeTokenRegistry();
        foreach (var (name, _) in AppColors) {
            registry.Register(name);
        }
        return registry;
    }
}
