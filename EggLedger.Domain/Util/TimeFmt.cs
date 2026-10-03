namespace EggLedger.Domain.Util;

public static class TimeFmt {
    public static double TimeToUnix(DateTimeOffset t) {

        var unixNano = t.ToUnixTimeMilliseconds() * 1_000_000L + t.Ticks % 10_000L * 100L;
        return unixNano / 1e9;
    }

    public static DateTimeOffset UnixToTime(double t) {
        var sec = Math.Truncate(t);
        var dec = t - sec;
        var nanos = (long)(dec * 1e9);
        return DateTimeOffset.FromUnixTimeSeconds((long)sec).AddTicks(nanos / 100L);
    }
}
