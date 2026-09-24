namespace EggLedger.Desktop.Tests;

internal sealed class TestClock(DateTimeOffset now) : TimeProvider {
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
