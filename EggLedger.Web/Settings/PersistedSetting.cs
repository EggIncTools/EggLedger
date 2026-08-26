using System.Globalization;
using EggLedger.Web.Data;

namespace EggLedger.Web.Settings;

public sealed class PersistedSetting<T>(
    IndexedDbSettings settings,
    string key,
    T defaultValue,
    Func<string, T?> parse,
    Func<T, string> format) {
    private readonly T _fallback = defaultValue;

    public event Action? Changed;

    public T Value { get; private set; } = defaultValue;

    public async Task LoadAsync() {
        var all = await settings.GetAllSettingsAsync();
        Value = all.TryGetValue(key, out var raw) && parse(raw) is { } parsed ? parsed : _fallback;
    }

    public async Task SetAsync(T value) {
        Value = value;
        await settings.SetSettingAsync(key, format(value));
        Changed?.Invoke();
    }
}

public static class PersistedSetting {
    public static PersistedSetting<bool> Bool(IndexedDbSettings settings, string key, bool defaultValue = false) =>
        new(settings, key, defaultValue,
            raw => bool.TryParse(raw, out var v) ? v : defaultValue,
            v => v ? "true" : "false");

    public static PersistedSetting<int> Int(IndexedDbSettings settings, string key, int defaultValue = 0) =>
        new(settings, key, defaultValue,
            raw => int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : defaultValue,
            v => v.ToString(CultureInfo.InvariantCulture));

    public static PersistedSetting<string> String(IndexedDbSettings settings, string key, string defaultValue = "") =>
        new(settings, key, defaultValue, raw => raw, v => v);

    public static PersistedSetting<TValue> Enum<TValue>(IndexedDbSettings settings, string key, TValue defaultValue)
        where TValue : struct, System.Enum =>
        new(settings, key, defaultValue,
            raw => System.Enum.TryParse<TValue>(raw, true, out var v) && System.Enum.IsDefined(v) ? v : defaultValue,
            v => v.ToString());
}
