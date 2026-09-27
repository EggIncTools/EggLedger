namespace EggLedger.Web.Platform;

public interface IUserTimeZoneProvider {
    TimeZoneInfo TimeZone { get; }

    event Action? Changed;

    Task EnsureUpToDateAsync();
}
