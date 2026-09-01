using System.Globalization;
using WebCondition = EggLedger.Web.Missions.FilterCondition;

namespace EggLedger.Web.Missions.Model;



public static class FilterCodec {
    public static Condition? FromLegacyCondition(WebCondition c) => ParseCondition(c.TopLevel, c.Op, c.Val);


    public static Condition? ParseCondition(string topLevel, string op, string val) {
        var c = new WebCondition(topLevel, op, val);
        if (string.IsNullOrEmpty(c.TopLevel)) {
            return null;
        }

        return c.TopLevel switch {
            "buggedcap" => new Condition(FilterField.BuggedCap, BoolOp(c.Val), new FilterValue.Flag(c.Val == "true")),
            "dubcap" => new Condition(FilterField.DubCap, BoolOp(c.Val), new FilterValue.Flag(c.Val == "true")),
            "ship" => EnumCond(FilterField.Ship, c),
            "duration" => EnumCond(FilterField.DurationType, c),
            "farm" or "type" => EnumCond(FilterField.MissionType, c),
            "target" => EnumCond(FilterField.Target, c),
            "level" => NumberCond(FilterField.Level, c),
            "capacity" => NumberCond(FilterField.Capacity, c),
            "launchDT" => DateCond(FilterField.LaunchDate, c),
            "returnDT" => DateCond(FilterField.ReturnDate, c),
            "drops" => DropCond(c),
            _ => null,
        };
    }

    private static Condition? EnumCond(FilterField field, WebCondition c) {
        if (!int.TryParse(c.Val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var code)) {
            return null;
        }
        return new Condition(field, ParseOp(c.Op), new FilterValue.EnumValue(code));
    }

    private static Condition? NumberCond(FilterField field, WebCondition c) {
        if (!double.TryParse(c.Val, NumberStyles.Float, CultureInfo.InvariantCulture, out var n)) {
            return null;
        }
        return new Condition(field, ParseOp(c.Op), new FilterValue.Number(n));
    }

    private static Condition? DateCond(FilterField field, WebCondition c) {
        if (!TryParseDay(c.Val, out var day)) {
            return null;
        }
        return new Condition(field, ParseDateOp(c.Op), new FilterValue.Day(day));
    }

    private static Condition DropCond(WebCondition c) {
        var op = c.Op == "dnc" ? FilterOperator.NotContains : FilterOperator.Contains;
        return new Condition(FilterField.Drops, op, new DropFilterValue(DecodeDropGlob(c.Val)));
    }

    private static FilterOperator BoolOp(string val) => val == "false" ? FilterOperator.IsFalse : FilterOperator.IsTrue;

    private static FilterOperator ParseOp(string op) => op switch {
        "=" => FilterOperator.Equals,
        "!=" => FilterOperator.NotEquals,
        ">" => FilterOperator.Greater,
        "<" => FilterOperator.Less,
        ">=" => FilterOperator.GreaterOrEqual,
        "<=" => FilterOperator.LessOrEqual,
        _ => FilterOperator.Equals,
    };

    private static FilterOperator ParseDateOp(string op) => op switch {
        "d=" or "=" => FilterOperator.Equals,
        ">" => FilterOperator.Greater,
        "<" => FilterOperator.Less,
        ">=" => FilterOperator.GreaterOrEqual,
        "<=" => FilterOperator.LessOrEqual,
        _ => FilterOperator.Equals,
    };

    public static DropMatch DecodeDropGlob(string glob) {
        var segs = (glob ?? "").Split('_');
        int? name = Seg(segs, 0);
        int? level = Seg(segs, 1);
        int? rarity = Seg(segs, 2);
        double? quality = SegD(segs, 3);
        return new DropMatch(name, level, rarity, quality);

        static int? Seg(string[] s, int i) {
            if (i >= s.Length || s[i] == "%" || s[i].Length == 0) {
                return null;
            }
            return int.TryParse(s[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;
        }

        static double? SegD(string[] s, int i) {
            if (i >= s.Length || s[i] == "%" || s[i].Length == 0) {
                return null;
            }
            return double.TryParse(s[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
        }
    }

    public static string EncodeDropGlob(DropMatch m) {
        string name = m.Name?.ToString(CultureInfo.InvariantCulture) ?? "%";
        string level = m.Level?.ToString(CultureInfo.InvariantCulture) ?? "%";
        string rarity = m.Rarity?.ToString(CultureInfo.InvariantCulture) ?? "%";
        string quality = m.Quality?.ToString(CultureInfo.InvariantCulture) ?? "%";
        return $"{name}_{level}_{rarity}_{quality}";
    }

    private static bool TryParseDay(string s, out DateOnly day) =>
        DateOnly.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out day);
}
