using System.Globalization;

namespace EggLedger.Domain.Util;

public static class Format {
    private static readonly string[] Suffixes = [
        "", "K", "M", "B", "T", "q", "Q", "s", "S", "o", "N", "d", "U", "D", "Td", "qd", "Qd", "sd", "Sd", "od", "Nd",
    ];

    public static string Addendum(int oom) => oom > 20 ? "!" : Suffixes[oom];

    public static string AbbreviateFloat(double v) {
        var vCopy = v;
        var ooms = 0;
        while (vCopy >= 1e3 && ooms < 20) {
            vCopy /= 1e3;
            ooms++;
        }
        int precision = vCopy < 10.0 ? 2 : vCopy < 100.0 ? 1 : 0;
        var formatted = vCopy.ToString("F" + precision.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        return formatted + Addendum(ooms);
    }
}
