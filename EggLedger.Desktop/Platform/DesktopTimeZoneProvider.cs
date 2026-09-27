using EggLedger.Web.Platform;

namespace EggLedger.Desktop.Platform;

public sealed class DesktopTimeZoneProvider : IUserTimeZoneProvider {
    public TimeZoneInfo TimeZone => TimeZoneInfo.Local;

    public event Action? Changed {
        add { }
        remove { }
    }

    public Task EnsureUpToDateAsync() => Task.CompletedTask;
}
