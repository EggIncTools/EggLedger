namespace EggLedger.Web.Tests.Data;

public sealed class ManualClock(DateTimeOffset now) : TimeProvider {
    public ManualClock() : this(new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero)) {
    }

    public DateTimeOffset UtcNow { get; set; } = now;

    public static ManualClock FromUnixSeconds(long seconds) => new(DateTimeOffset.FromUnixTimeSeconds(seconds));

    public override DateTimeOffset GetUtcNow() => UtcNow;

    public void Advance(TimeSpan delta) => UtcNow += delta;

    public void SetUnixSeconds(long seconds) => UtcNow = DateTimeOffset.FromUnixTimeSeconds(seconds);
}
