using EggLedger.Web.Data;
using EggLedger.Web.Settings;
using EggLedger.Web.Tests.Data;

namespace EggLedger.Web.Tests.Settings;

public sealed class PersistedSettingTests {
    private enum Flavor {
        Plain,
        Spicy,
        Sweet
    }

    private static IndexedDbSettings NewStore() {
        return new IndexedDbSettings(new FakeIndexedDb());
    }

    [Fact]
    public async Task Load_KeyAbsent_KeepsDefault() {
        var setting = PersistedSetting.Bool(NewStore(), "never_written", true);

        await setting.LoadAsync();

        Assert.True(setting.Value);
    }

    [Fact]
    public async Task Set_ThenLoadOnFreshInstance_RoundTrips() {
        var store = NewStore();
        var writer = PersistedSetting.Int(store, "worker_count", 1);

        await writer.SetAsync(7);

        var reader = PersistedSetting.Int(store, "worker_count", 1);
        await reader.LoadAsync();
        Assert.Equal(7, reader.Value);
        Assert.Equal("7", (await store.GetAllSettingsAsync())["worker_count"]);
    }

    [Fact]
    public async Task Set_WritesTheFormattedValue() {
        var store = NewStore();
        var setting = PersistedSetting.Bool(store, "show_drops", false);

        await setting.SetAsync(true);

        Assert.Equal("true", (await store.GetAllSettingsAsync())["show_drops"]);
        Assert.True(setting.Value);
    }

    [Fact]
    public async Task Load_UnparsableBool_FallsBackToDefault() {
        var store = NewStore();
        await store.SetSettingAsync("show_drops", "banana");
        var setting = PersistedSetting.Bool(store, "show_drops", true);

        await setting.LoadAsync();

        Assert.True(setting.Value);
    }

    [Fact]
    public async Task Load_UnparsableInt_FallsBackToDefault() {
        var store = NewStore();
        await store.SetSettingAsync("worker_count", "lots");
        var setting = PersistedSetting.Int(store, "worker_count", 3);

        await setting.LoadAsync();

        Assert.Equal(3, setting.Value);
    }

    [Fact]
    public async Task Load_UnknownEnumName_FallsBackToDefault() {
        var store = NewStore();
        await store.SetSettingAsync("flavor", "umami");
        var setting = PersistedSetting.Enum(store, "flavor", Flavor.Sweet);

        await setting.LoadAsync();

        Assert.Equal(Flavor.Sweet, setting.Value);
    }

    [Fact]
    public async Task Load_OutOfRangeEnumNumber_FallsBackToDefault() {
        var store = NewStore();
        await store.SetSettingAsync("flavor", "42");
        var setting = PersistedSetting.Enum(store, "flavor", Flavor.Plain);

        await setting.LoadAsync();

        Assert.Equal(Flavor.Plain, setting.Value);
    }

    [Fact]
    public async Task Enum_RoundTripsCaseInsensitively() {
        var store = NewStore();
        var writer = PersistedSetting.Enum(store, "flavor", Flavor.Plain);
        await writer.SetAsync(Flavor.Spicy);
        Assert.Equal("Spicy", (await store.GetAllSettingsAsync())["flavor"]);

        await store.SetSettingAsync("flavor", "spicy");
        var reader = PersistedSetting.Enum(store, "flavor", Flavor.Plain);
        await reader.LoadAsync();

        Assert.Equal(Flavor.Spicy, reader.Value);
    }

    [Fact]
    public async Task String_LoadsStoredValueAndDefaultsWhenAbsent() {
        var store = NewStore();
        var absent = PersistedSetting.String(store, "backup_dest_path", "C:/fallback");
        await absent.LoadAsync();
        Assert.Equal("C:/fallback", absent.Value);

        await absent.SetAsync("D:/exports");
        var reader = PersistedSetting.String(store, "backup_dest_path", "C:/fallback");
        await reader.LoadAsync();
        Assert.Equal("D:/exports", reader.Value);
    }

    [Fact]
    public async Task CustomParse_ReturningNull_FallsBackToDefault() {
        var store = NewStore();
        await store.SetSettingAsync("nickname", "raw");
        var setting = new PersistedSetting<string>(store, "nickname", "fallback", _ => null, v => v);

        await setting.LoadAsync();

        Assert.Equal("fallback", setting.Value);
    }

    [Fact]
    public async Task Set_RaisesChanged() {
        var setting = PersistedSetting.Bool(NewStore(), "show_drops");
        var fired = 0;
        setting.Changed += () => fired++;

        await setting.SetAsync(true);
        await setting.SetAsync(false);

        Assert.Equal(2, fired);
    }

    [Fact]
    public async Task Load_DoesNotRaiseChanged() {
        var store = NewStore();
        await store.SetSettingAsync("show_drops", "true");
        var setting = PersistedSetting.Bool(store, "show_drops");
        var fired = 0;
        setting.Changed += () => fired++;

        await setting.LoadAsync();

        Assert.True(setting.Value);
        Assert.Equal(0, fired);
    }
}
