namespace EggLedger.Domain.Tests;

internal sealed class FixedClock(DateTimeOffset now) : TimeProvider {
    public override DateTimeOffset GetUtcNow() => now;
}
