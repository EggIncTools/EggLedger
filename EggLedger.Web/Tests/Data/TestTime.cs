namespace EggLedger.Web.Tests.Data;

public static class TestTime {
    public static FakeTimeProvider New() => new(new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero));

    public static FakeTimeProvider AtUnix(long seconds) => new(DateTimeOffset.FromUnixTimeSeconds(seconds));
}
