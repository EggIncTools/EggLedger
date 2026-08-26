using System.Globalization;

namespace EggLedger.Domain.Reports;


internal sealed class MissionRowPredicate(
    ReportDefinition def,
    ILookup<(string Player, string Mission), ArtifactDropRowData> dropsByMission) {

    public bool PassesFilters(MissionRowData m) {
        foreach (var c in def.Filters.And) {
            if (!EvalMission(c, m)) {
                return false;
            }
        }
        foreach (var group in def.Filters.Or) {
            var any = false;
            var hadClause = false;
            foreach (var c in group) {
                if (!IsMissionScope(c)) {
                    continue;
                }
                hadClause = true;
                if (EvalMission(c, m)) {
                    any = true;
                }
            }
            if (hadClause && !any) {
                return false;
            }
        }
        return true;
    }


    public bool PassesArtifactFilters(ArtifactDropRowData d) {
        foreach (var c in def.Filters.And) {
            if (IsArtifactScope(c) && !EvalArtifact(c, d)) {
                return false;
            }
        }
        foreach (var group in def.Filters.Or) {
            var any = false;
            var hadClause = false;
            foreach (var c in group) {
                if (!IsArtifactScope(c)) {
                    continue;
                }
                hadClause = true;
                if (EvalArtifact(c, d)) {
                    any = true;
                }
            }
            if (hadClause && !any) {
                return false;
            }
        }
        return true;
    }

    private static readonly HashSet<string> ArtifactTopLevels = new(StringComparer.Ordinal)
    {
        "artifact_rarity", "artifact_spec_type", "artifact_name", "artifact_tier", "artifact_quality",
    };

    private static bool IsArtifactScope(FilterCondition c) => ArtifactTopLevels.Contains(c.TopLevel);

    private static bool IsMissionScope(FilterCondition c) => !IsArtifactScope(c);



    private bool EvalMission(FilterCondition c, MissionRowData m) {
        return c.TopLevel switch {
            "dubcap" => m.IsDubCap == (c.Op == "true"),
            "buggedcap" => m.IsBuggedCap == (c.Op == "true"),
            "drops" => EvalDrops(c, m),
            "launchDT" => EvalDate(c, m.StartTimestamp),
            "returnDT" => EvalDate(c, m.ReturnTimestamp),
            "ship" => CompareNumeric(c, m.Ship),
            "duration" => CompareNumeric(c, m.DurationType),
            "level" => CompareNumeric(c, m.Level),
            "target" => CompareNumeric(c, m.Target),
            "type" => CompareNumeric(c, m.MissionType),
            _ => true,
        };
    }

    private static bool EvalArtifact(FilterCondition c, ArtifactDropRowData d) {

        if (c.Op is not ("=" or "!=" or ">" or "<" or ">=" or "<=")) {
            return true;
        }
        return c.TopLevel switch {
            "artifact_rarity" => IsInt(c.Val) && CompareLong(c.Op, d.Rarity, long.Parse(c.Val, CultureInfo.InvariantCulture)),
            "artifact_tier" => IsInt(c.Val) && CompareLong(c.Op, d.Level, long.Parse(c.Val, CultureInfo.InvariantCulture)),
            "artifact_name" => IsInt(c.Val) && CompareLong(c.Op, d.ArtifactId, long.Parse(c.Val, CultureInfo.InvariantCulture)),
            "artifact_quality" => IsFloat(c.Val) && CompareDouble(c.Op, d.Quality, double.Parse(c.Val, CultureInfo.InvariantCulture)),
            "artifact_spec_type" => CompareText(c.Op, d.SpecType, c.Val),
            _ => true,
        };
    }

    private bool EvalDrops(FilterCondition c, MissionRowData m) {
        if (c.Op is not "c" and not "dnc") {
            return true;
        }
        if (c.Val == "") {
            return true;
        }
        var parts = c.Val.Split('_');

        bool Matches(ArtifactDropRowData d) {
            if (parts.Length > 0 && parts[0] != "%" && parts[0] != "" && d.ArtifactId.ToString(CultureInfo.InvariantCulture) != parts[0]) {
                return false;
            }
            if (parts.Length > 1 && parts[1] != "%" && parts[1] != "" && d.Level.ToString(CultureInfo.InvariantCulture) != parts[1]) {
                return false;
            }
            if (parts.Length > 2 && parts[2] != "%" && parts[2] != "" && d.Rarity.ToString(CultureInfo.InvariantCulture) != parts[2]) {
                return false;
            }
            return true;
        }
        var exists = dropsByMission[(m.PlayerId, m.MissionId)].Any(Matches);
        return c.Op == "dnc" ? !exists : exists;
    }

    private static bool CompareNumeric(FilterCondition c, long actual) {
        if (c.Op is not ("=" or "!=" or ">" or "<" or ">=" or "<=")) {
            return true;
        }
        if (!IsInt(c.Val)) {

            return true;
        }
        return CompareLong(c.Op, actual, long.Parse(c.Val, CultureInfo.InvariantCulture));
    }

    private static bool EvalDate(FilterCondition c, long actualUnix) {
        long target;
        switch (c.Op) {
            case ">":
            case "<":
            case ">=":
            case "<=":
            case "=":
                if (!TimeBucket.TryParseDateToUnix(c.Val, out target)) {
                    return true;
                }
                break;
            case "d=":
                if (!TimeBucket.TryParseDateToUnix(c.Val, out target)) {
                    return true;
                }
                return actualUnix == target;
            default:
                return true;
        }
        return c.Op switch {
            ">" => actualUnix > target,
            "<" => actualUnix < target,
            ">=" => actualUnix >= target,
            "<=" => actualUnix <= target,
            "=" => actualUnix == target,
            _ => true,
        };
    }

    private static bool CompareLong(string op, long a, long b) => op switch {
        "=" => a == b,
        "!=" => a != b,
        ">" => a > b,
        "<" => a < b,
        ">=" => a >= b,
        "<=" => a <= b,
        _ => true,
    };

    private static bool CompareDouble(string op, double a, double b) => op switch {
        "=" => a == b,
        "!=" => a != b,
        ">" => a > b,
        "<" => a < b,
        ">=" => a >= b,
        "<=" => a <= b,
        _ => true,
    };

    private static bool CompareText(string op, string a, string b) => op switch {
        "=" => string.Equals(a, b, StringComparison.Ordinal),
        "!=" => !string.Equals(a, b, StringComparison.Ordinal),
        ">" => string.CompareOrdinal(a, b) > 0,
        "<" => string.CompareOrdinal(a, b) < 0,
        ">=" => string.CompareOrdinal(a, b) >= 0,
        "<=" => string.CompareOrdinal(a, b) <= 0,
        _ => true,
    };

    private static bool IsInt(string s) =>
        long.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _);

    private static bool IsFloat(string s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
}
