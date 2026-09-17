using System.Globalization;

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

    public static string HumanizeTime(DateTimeOffset t, DateTimeOffset? now = null) {
        var reference = now ?? DateTimeOffset.UtcNow;
        var delta = reference - t;
        if (delta < TimeSpan.FromMinutes(1)) {
            return "just now";
        }
        if (delta < TimeSpan.FromHours(1)) {
            return string.Format(CultureInfo.InvariantCulture, "{0} minutes ago", (int)delta.TotalMinutes);
        }
        if (delta < TimeSpan.FromHours(24)) {
            return string.Format(CultureInfo.InvariantCulture, "{0} hours ago", (int)delta.TotalHours);
        }
        if (delta < TimeSpan.FromHours(30 * 24)) {
            return string.Format(CultureInfo.InvariantCulture, "{0} days ago", (int)(delta.TotalHours / 24));
        }
        if (delta < TimeSpan.FromHours(365 * 24)) {
            return string.Format(CultureInfo.InvariantCulture, "{0} months ago", (int)(delta.TotalHours / (24 * 30)));
        }
        return string.Format(CultureInfo.InvariantCulture, "{0} years ago", (int)(delta.TotalHours / (24 * 365)));
    }
}
