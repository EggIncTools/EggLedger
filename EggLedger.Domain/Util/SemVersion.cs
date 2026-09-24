using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace EggLedger.Domain.Util;

public sealed partial class SemVersion : IComparable<SemVersion>, IEquatable<SemVersion> {

    [GeneratedRegex(
        @"^v?([0-9]+(\.[0-9]+)*?)" +
        @"(-([0-9]+[0-9A-Za-z\-~]*(\.[0-9A-Za-z\-~]+)*)|(-?([A-Za-z\-~]+[0-9A-Za-z\-~]*(\.[0-9A-Za-z\-~]+)*)))?" +
        @"(\+([0-9A-Za-z\-~]+(\.[0-9A-Za-z\-~]+)*))?" +
        @"?$")]
    private static partial Regex VersionRegex();

    private readonly long[] _segments;

    private SemVersion(long[] segments, string pre, string metadata, string original) {
        _segments = segments;
        Prerelease = pre;
        Metadata = metadata;
        Original = original;
    }

    public IReadOnlyList<long> Segments => _segments;

    public string Prerelease { get; }

    public string Metadata { get; }

    public string Original { get; }

    public static bool TryParse(string? input, out SemVersion? version) {
        version = null;
        if (input is null) {
            return false;
        }

        var match = VersionRegex().Match(input);
        if (!match.Success) {
            return false;
        }

        var segmentsStr = match.Groups[1].Value.Split('.');
        var segments = new long[segmentsStr.Length];
        for (var i = 0; i < segmentsStr.Length; i++) {
            if (!long.TryParse(segmentsStr[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var val)) {
                return false;
            }
            segments[i] = val;
        }

        if (segments.Length < 3) {
            var padded = new long[3];
            Array.Copy(segments, padded, segments.Length);
            segments = padded;
        }

        var pre = match.Groups[7].Value;
        if (pre.Length == 0) {
            pre = match.Groups[4].Value;
        }

        var metadata = match.Groups[10].Value;

        version = new SemVersion(segments, pre, metadata, input);
        return true;
    }

    public static SemVersion Parse(string input)
        => TryParse(input, out var v) && v is not null
            ? v
            : throw new FormatException($"Malformed version: {input}");

    public string Canonical() {
        var sb = new StringBuilder();
        for (var i = 0; i < _segments.Length; i++) {
            if (i > 0) {
                sb.Append('.');
            }
            sb.Append(_segments[i].ToString(CultureInfo.InvariantCulture));
        }
        if (Prerelease.Length > 0) {
            sb.Append('-').Append(Prerelease);
        }
        if (Metadata.Length > 0) {
            sb.Append('+').Append(Metadata);
        }
        return sb.ToString();
    }

    public int CompareTo(SemVersion? other) {
        ArgumentNullException.ThrowIfNull(other);

        if (Canonical() == other.Canonical()) {
            return 0;
        }

        var self = _segments;
        var oth = other._segments;

        if (SegmentsEqual(self, oth)) {
            var preSelf = Prerelease;
            var preOther = other.Prerelease;
            if (preSelf.Length == 0 && preOther.Length == 0) {
                return 0;
            }
            if (preSelf.Length == 0) {
                return 1;
            }
            return preOther.Length == 0 ? -1 : ComparePrereleases(preSelf, preOther);
        }

        var lenSelf = self.Length;
        var lenOther = oth.Length;
        var hS = Math.Max(lenSelf, lenOther);
        for (var i = 0; i < hS; i++) {
            if (i > lenSelf - 1) {
                if (!AllZero(oth, i)) {
                    return -1;
                }
                break;
            }
            if (i > lenOther - 1) {
                if (!AllZero(self, i)) {
                    return 1;
                }
                break;
            }
            var lhs = self[i];
            var rhs = oth[i];
            if (lhs == rhs) {
                continue;
            }
            return lhs < rhs ? -1 : 1;
        }

        return 0;
    }

    public bool GreaterThan(SemVersion other) => CompareTo(other) > 0;
    public bool LessThan(SemVersion other) => CompareTo(other) < 0;
    public bool GreaterThanOrEqual(SemVersion other) => CompareTo(other) >= 0;
    public bool LessThanOrEqual(SemVersion other) => CompareTo(other) <= 0;

    public bool Equals(SemVersion? other) => other is not null && CompareTo(other) == 0;
    public override bool Equals(object? obj) => obj is SemVersion v && Equals(v);

    public override int GetHashCode() {

        var hash = new HashCode();
        foreach (var s in _segments) {
            hash.Add(s);
        }
        hash.Add(Prerelease);
        return hash.ToHashCode();
    }

    public override string ToString() => Canonical();

    private static bool SegmentsEqual(long[] a, long[] b) {
        if (a.Length != b.Length) {
            return false;
        }
        for (var i = 0; i < a.Length; i++) {
            if (a[i] != b[i]) {
                return false;
            }
        }
        return true;
    }

    private static bool AllZero(long[] segs, int from) {
        for (var i = from; i < segs.Length; i++) {
            if (segs[i] != 0) {
                return false;
            }
        }
        return true;
    }


    private static int ComparePrereleases(string v, string other) {
        if (v == other) {
            return 0;
        }

        var selfParts = v.Split('.');
        var otherParts = other.Split('.');
        var biggest = Math.Max(selfParts.Length, otherParts.Length);

        for (var i = 0; i < biggest; i++) {
            var partSelf = i < selfParts.Length ? selfParts[i] : "";
            var partOther = i < otherParts.Length ? otherParts[i] : "";
            var c = ComparePart(partSelf, partOther);
            if (c != 0) {
                return c;
            }
        }
        return 0;
    }


    private static int ComparePart(string preSelf, string preOther) {
        if (preSelf == preOther) {
            return 0;
        }

        var selfNumeric = long.TryParse(preSelf, NumberStyles.Integer, CultureInfo.InvariantCulture, out var selfInt);
        var otherNumeric = long.TryParse(preOther, NumberStyles.Integer, CultureInfo.InvariantCulture, out var otherInt);

        if (preSelf.Length == 0) {
            return otherNumeric ? -1 : 1;
        }
        if (preOther.Length == 0) {
            return selfNumeric ? 1 : -1;
        }

        if (selfNumeric && !otherNumeric) {
            return -1;
        }
        if (!selfNumeric && otherNumeric) {
            return 1;
        }
        if (!selfNumeric && !otherNumeric && string.CompareOrdinal(preSelf, preOther) > 0) {
            return 1;
        }
        return selfNumeric && otherNumeric && selfInt > otherInt ? 1 : -1;
    }
}
