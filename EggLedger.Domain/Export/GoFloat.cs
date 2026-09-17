using System.Globalization;

namespace EggLedger.Domain.Export;

public static class GoFloat {
    public static string FormatF(double v) {
        if (double.IsNaN(v)) {
            return "NaN";
        }
        if (double.IsPositiveInfinity(v)) {
            return "+Inf";
        }
        if (double.IsNegativeInfinity(v)) {
            return "-Inf";
        }



        string s = v.ToString("R", CultureInfo.InvariantCulture);
        if (s.IndexOf('E') < 0 && s.IndexOf('e') < 0) {
            return s;
        }
        return ExpandExponential(s);
    }

    public static string FormatG(double v) {
        if (double.IsNaN(v)) {
            return "NaN";
        }
        if (double.IsPositiveInfinity(v)) {
            return "+Inf";
        }
        if (double.IsNegativeInfinity(v)) {
            return "-Inf";
        }


        string shortest = v.ToString("R", CultureInfo.InvariantCulture);


        return NormalizeG(v, shortest);
    }

    private static string ExpandExponential(string s) {
        bool neg = false;
        int i = 0;
        if (s[0] == '-') {
            neg = true;
            i = 1;
        } else if (s[0] == '+') {
            i = 1;
        }

        int eIdx = s.IndexOfAny(['E', 'e'], i);
        string mantissa = s[i..eIdx];
        int exp = int.Parse(s[(eIdx + 1)..], CultureInfo.InvariantCulture);

        string intPart;
        string fracPart;
        int dot = mantissa.IndexOf('.');
        if (dot < 0) {
            intPart = mantissa;
            fracPart = "";
        } else {
            intPart = mantissa[..dot];
            fracPart = mantissa[(dot + 1)..];
        }

        string digits = intPart + fracPart;
        int pointPos = intPart.Length + exp;

        string result = pointPos <= 0
            ? "0." + new string('0', -pointPos) + digits
            : pointPos >= digits.Length ? digits + new string('0', pointPos - digits.Length) :
                digits[..pointPos] + "." + digits[pointPos..];
        result = TrimDecimal(result);
        return neg ? "-" + result : result;
    }

    private static string TrimDecimal(string s) {
        if (s.IndexOf('.') < 0) {
            return s;
        }
        s = s.TrimEnd('0');
        if (s.EndsWith('.')) {
            s = s[..^1];
        }
        return s;
    }

    private static string NormalizeG(double v, string shortest) {
        if (v == 0) {
            return shortest;
        }


        string plain = shortest.IndexOfAny(['E', 'e']) >= 0 ? ExpandExponential(shortest) : shortest;
        string abs = plain.StartsWith('-') ? plain[1..] : plain;
        bool neg = plain.StartsWith('-');



        int dot = abs.IndexOf('.');
        string intPart = dot < 0 ? abs : abs[..dot];
        string fracPart = dot < 0 ? "" : abs[(dot + 1)..];

        int exp;
        if (intPart != "0" && intPart.Length > 0) {
            exp = intPart.Length - 1;
        } else {

            int lead = 0;
            while (lead < fracPart.Length && fracPart[lead] == '0') {
                lead++;
            }
            exp = -(lead + 1);
        }


        if (exp is < -4 or >= 21) {
            return ToGoExponential(neg, intPart, fracPart, exp);
        }
        return plain;
    }

    private static string ToGoExponential(bool neg, string intPart, string fracPart, int exp) {
        string digits = (intPart + fracPart).TrimStart('0');
        if (digits.Length == 0) {
            digits = "0";
        }
        digits = digits.TrimEnd('0');
        if (digits.Length == 0) {
            digits = "0";
        }

        string mantissa = digits.Length == 1
            ? digits
            : digits[..1] + "." + digits[1..];

        string sign = exp < 0 ? "-" : "+";
        string expStr = Math.Abs(exp).ToString("D2", CultureInfo.InvariantCulture);
        string body = mantissa + "e" + sign + expStr;
        return neg ? "-" + body : body;
    }
}
