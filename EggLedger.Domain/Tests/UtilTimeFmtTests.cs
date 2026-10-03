using EggLedger.Domain.Util;

namespace EggLedger.Domain.Tests;

public class UtilTimeFmtTests {
    [Fact]
    public void UnixRoundTrip() {
        var unix = 1_700_000_000.5;
        var t = TimeFmt.UnixToTime(unix);
        Assert.True(Math.Abs(TimeFmt.TimeToUnix(t) - unix) < 1e-6);
    }
}
