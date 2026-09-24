using System.Globalization;
using System.Runtime.InteropServices;
using EggLedger.Domain.MissionPacking;

namespace EggLedger.Domain.Reports;

internal sealed class InMemoryMissionDb : IMissionDb {

    private const string CapWeightMarker = "cap_weight";
    private const string BucketMarker = "AS bucket";
    private const string GrpMarker = "AS grp";
    private const string AirtimeSumMarker = "SUM(CAST(m.return_timestamp - m.start_timestamp AS REAL) / 3600.0)";
    private const string FuelSumMarker = "SUM(f.amount)";


    private const string ArtifactJoinMarker = "JOIN mission m ON d.mission_id = m.mission_id";
    private const string CountMarker = "COUNT(*)";
    private const string GroupByMarker = "GROUP BY ";

    private readonly ReportDefinition _def;
    private readonly List<MissionRowData> _missions;
    private readonly List<ArtifactDropRowData> _drops;
    private readonly List<FuelRowData> _fuel;
    private readonly IWeightData _weights;
    private readonly TimeProvider _time;


    private readonly ILookup<(string Player, string Mission), ArtifactDropRowData> _dropsByMission;
    private readonly ILookup<(string Player, string Mission), FuelRowData> _fuelByMission;

    private readonly MissionRowPredicate _predicate;

    public InMemoryMissionDb(
        ReportDefinition def,
        IReadOnlyList<MissionRowData> missions,
        IReadOnlyList<ArtifactDropRowData> drops,
        IReadOnlyList<FuelRowData> fuel,
        IWeightData weights,
        TimeProvider? time = null) {
        _def = def;
        _time = time ?? TimeProvider.System;
        _missions = [.. missions];
        _drops = [.. drops];
        _fuel = [.. fuel];
        _weights = weights;
        _dropsByMission = _drops.ToLookup(d => (d.PlayerId, d.MissionId));
        _fuelByMission = _fuel.ToLookup(f => (f.PlayerId, f.MissionId));
        _predicate = new MissionRowPredicate(_def, _dropsByMission);
    }

    public IReadOnlyList<object?[]> Query(string sql, IReadOnlyList<object?> args) {
        if (sql.Contains(FuelSumMarker, StringComparison.Ordinal)) {
            return FuelAggregate();
        }

        var weighted = sql.Contains(CapWeightMarker, StringComparison.Ordinal);
        var hasBucket = sql.Contains(BucketMarker, StringComparison.Ordinal);
        var hasGrp = sql.Contains(GrpMarker, StringComparison.Ordinal);
        var airtimeDenom = sql.Contains(AirtimeSumMarker, StringComparison.Ordinal);

        var joinDrops = sql.Contains(ArtifactJoinMarker, StringComparison.Ordinal);

        if (weighted) {
            if (hasBucket && hasGrp) {
                return WeightedTimePivot();
            }
            if (hasBucket) {
                return WeightedTimeSeries();
            }
            return _def.SecondaryGroupBy != "" ? WeightedPivot() : WeightedAggregate();
        }

        if (hasBucket && hasGrp) {
            return TimePivotCount(joinDrops);
        }
        if (hasBucket) {
            return TimeSeriesCount(joinDrops);
        }

        if (airtimeDenom) {
            return _def.SecondaryGroupBy != "" && Is2DAirtimeQuery(sql)
                ? Airtime2D(joinDrops)
                : Airtime1D(joinDrops);
        }

        if (!sql.Contains(CountMarker, StringComparison.Ordinal)
            || !sql.Contains(GroupByMarker, StringComparison.Ordinal)) {
            throw new InvalidOperationException($"unrecognized query shape: {sql}");
        }
        return Is2DCountQuery(sql) ? Count2D(joinDrops) : Count1D(joinDrops);
    }


    private bool Is2DCountQuery(string sql) {
        if (_def.SecondaryGroupBy == "") {
            return false;
        }
        var col1 = QueryBuilder.GroupByColumn(_def.GroupBy);
        var col2 = QueryBuilder.GroupByColumn(_def.SecondaryGroupBy);
        return col1 != "" && col2 != ""
            && sql.Contains("CAST(" + col1 + " AS TEXT), CAST(" + col2 + " AS TEXT)", StringComparison.Ordinal);
    }

    private bool Is2DAirtimeQuery(string sql) {
        if (_def.SecondaryGroupBy == "") {
            return false;
        }
        var col2 = QueryBuilder.GroupByColumn(_def.SecondaryGroupBy);
        return col2 != "" && sql.Contains("CAST(" + col2 + " AS TEXT), SUM", StringComparison.Ordinal);
    }


    private IEnumerable<MissionRowData> FilteredMissions() =>
        _missions.Where(m => m.PlayerId == _def.AccountId && _predicate.PassesFilters(m));


    private IEnumerable<(MissionRowData M, ArtifactDropRowData D)> FilteredJoin() {
        foreach (var m in FilteredMissions()) {
            foreach (var d in _dropsByMission[(m.PlayerId, m.MissionId)]) {
                if (d.DropIndex >= 0 && _predicate.PassesArtifactFilters(d)) {
                    yield return (m, d);
                }
            }
        }
    }

    private List<object?[]> Count1D(bool joinDrops) {
        var col = QueryBuilder.GroupByColumn(_def.GroupBy);
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var key in GroupKeys1D(col, joinDrops)) {
            ref var cur = ref CollectionsMarshal.GetValueRefOrAddDefault(counts, key, out var exists);
            if (!exists) {
                order.Add(key);
            }
            cur++;
        }

        var rows = order
            .Select((k, i) => (k, i, c: counts[k]))
            .OrderByDescending(x => x.c)
            .ThenBy(x => x.i)
            .Select(x => new object?[] { x.k, x.c })
            .ToList();
        return rows;
    }

    private List<object?[]> FuelAggregate() {
        var col = QueryBuilder.GroupByColumn(_def.GroupBy);
        var sums = new Dictionary<string, double>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var m in FilteredMissions()) {
            if (MissionFuelTotal(m) is not double amount) {
                continue;
            }
            var key = ColValue(m, col);
            ref var cur = ref CollectionsMarshal.GetValueRefOrAddDefault(sums, key, out var exists);
            if (!exists) {
                order.Add(key);
            }
            cur += amount;
        }
        return [.. order
            .Select((k, i) => (k, i, s: sums[k]))
            .OrderByDescending(x => x.s)
            .ThenBy(x => x.i)
            .Select(x => new object?[] { x.k, x.s })];
    }

    private double? MissionFuelTotal(MissionRowData m) {
        var recorded = _fuelByMission[(m.PlayerId, m.MissionId)];
        if (recorded.Any()) {
            return recorded.Sum(f => f.Amount);
        }
        var synthesized = ShipFuelCosts.For(m.Ship, m.DurationType);
        return synthesized.Count > 0 ? synthesized.Sum(f => f.Amount) : null;
    }

    private List<object?[]> Count2D(bool joinDrops) {
        var col1 = QueryBuilder.GroupByColumn(_def.GroupBy);
        var col2 = QueryBuilder.GroupByColumn(_def.SecondaryGroupBy);
        var counts = new Dictionary<(string K1, string K2), long>();
        var order = new List<(string K1, string K2)>();
        foreach (var (k1, k2) in GroupKeys2D(col1, col2, joinDrops)) {
            var key = (k1, k2);
            ref var cur = ref CollectionsMarshal.GetValueRefOrAddDefault(counts, key, out var exists);
            if (!exists) {
                order.Add(key);
            }
            cur++;
        }

        var rows = order
            .OrderBy(k => k, KeyPairComparer(col1, col2))
            .Select(k => new object?[] { k.K1, k.K2, counts[k] })
            .ToList();
        return rows;
    }

    private List<object?[]> Airtime1D(bool joinDrops) {
        var col = QueryBuilder.GroupByColumn(_def.GroupBy);
        var sums = new Dictionary<string, double>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var m in AirtimeRows(joinDrops, col, null)) {
            var key = ColValue(m, col);
            ref var cur = ref CollectionsMarshal.GetValueRefOrAddDefault(sums, key, out var exists);
            if (!exists) {
                order.Add(key);
            }
            cur += (m.ReturnTimestamp - m.StartTimestamp) / 3600.0;
        }
        return [.. order.Select(k => new object?[] { k, sums[k] })];
    }

    private List<object?[]> Airtime2D(bool joinDrops) {
        var col1 = QueryBuilder.GroupByColumn(_def.GroupBy);
        var col2 = QueryBuilder.GroupByColumn(_def.SecondaryGroupBy);
        var sums = new Dictionary<(string K1, string K2), double>();
        var order = new List<(string K1, string K2)>();
        foreach (var m in AirtimeRows(joinDrops, col1, col2)) {
            var key = (ColValue(m, col1), ColValue(m, col2));
            ref var cur = ref CollectionsMarshal.GetValueRefOrAddDefault(sums, key, out var exists);
            if (!exists) {
                order.Add(key);
            }
            cur += (m.ReturnTimestamp - m.StartTimestamp) / 3600.0;
        }
        return [.. order.Select(k => new object?[] { k.K1, k.K2, sums[k] })];
    }

    private IEnumerable<MissionRowData> AirtimeRows(bool joinDrops, string col1, string? col2) =>
        joinDrops
            || col1.StartsWith("d.", StringComparison.Ordinal)
            || (col2 is not null && col2.StartsWith("d.", StringComparison.Ordinal))
            ? FilteredJoin().Select(p => p.M)
            : FilteredMissions();

    private List<object?[]> TimeSeriesCount(bool joinDrops) {
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var m in FilteredBucketRows(joinDrops)) {
            var bucket = BucketLabel(m.StartTimestamp);
            CollectionsMarshal.GetValueRefOrAddDefault(counts, bucket, out _)++;
        }
        return [.. counts
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new object?[] { kv.Key, kv.Value })];
    }

    private List<object?[]> TimePivotCount(bool joinDrops) {
        var col2 = QueryBuilder.GroupByColumn(_def.SecondaryGroupBy);
        var counts = new Dictionary<(string Bucket, string Grp), long>();
        foreach (var (m, d) in FilteredBucketJoin(col2, joinDrops)) {
            var bucket = BucketLabel(m.StartTimestamp);
            var grp = col2.StartsWith("d.", StringComparison.Ordinal) ? ColValueDrop(d!, col2) : ColValue(m, col2);
            CollectionsMarshal.GetValueRefOrAddDefault(counts, (bucket, grp), out _)++;
        }
        return [.. counts
            .OrderBy(kv => kv.Key.Bucket, StringComparer.Ordinal)
            .ThenBy(kv => kv.Key.Grp, StringComparer.Ordinal)
            .Select(kv => new object?[] { kv.Key.Bucket, kv.Key.Grp, kv.Value })];
    }

    private List<object?[]> WeightedAggregate() {
        var col = QueryBuilder.GroupByColumn(_def.GroupBy);
        var groups = new Dictionary<(string Key, long ArtifactId, long Level), double>();
        var order = new List<(string Key, long ArtifactId, long Level)>();
        foreach (var (m, d) in WeightedJoin()) {
            var key = (col.StartsWith("d.", StringComparison.Ordinal) ? ColValueDrop(d, col) : ColValue(m, col), d.ArtifactId, d.Level);
            ref var cur = ref CollectionsMarshal.GetValueRefOrAddDefault(groups, key, out var exists);
            if (!exists) {
                order.Add(key);
            }
            cur += CapWeight(m);
        }
        var rows = order
            .Select(k => (k, cap: groups[k]))
            .OrderByDescending(x => x.cap)
            .Select(x => new object?[] { x.k.Key, x.k.ArtifactId, x.k.Level, x.cap })
            .ToList();
        return rows;
    }

    private List<object?[]> WeightedPivot() {
        var col1 = QueryBuilder.GroupByColumn(_def.GroupBy);
        var col2 = QueryBuilder.GroupByColumn(_def.SecondaryGroupBy);
        var groups = new Dictionary<(string K1, string K2, long ArtifactId, long Level), double>();
        var order = new List<(string K1, string K2, long ArtifactId, long Level)>();
        foreach (var (m, d) in WeightedJoin()) {
            var k1 = col1.StartsWith("d.", StringComparison.Ordinal) ? ColValueDrop(d, col1) : ColValue(m, col1);
            var k2 = col2.StartsWith("d.", StringComparison.Ordinal) ? ColValueDrop(d, col2) : ColValue(m, col2);
            var key = (k1, k2, d.ArtifactId, d.Level);
            ref var cur = ref CollectionsMarshal.GetValueRefOrAddDefault(groups, key, out var exists);
            if (!exists) {
                order.Add(key);
            }
            cur += CapWeight(m);
        }

        var rows = order
            .Select(k => new object?[] { k.K1, k.K2, k.ArtifactId, k.Level, groups[k] })
            .ToList();
        return rows;
    }

    private List<object?[]> WeightedTimeSeries() {
        var groups = new Dictionary<(string Bucket, long ArtifactId, long Level), double>();
        var order = new List<(string Bucket, long ArtifactId, long Level)>();
        foreach (var (m, d) in WeightedBucketJoin()) {
            var key = (BucketLabel(m.StartTimestamp), d.ArtifactId, d.Level);
            ref var cur = ref CollectionsMarshal.GetValueRefOrAddDefault(groups, key, out var exists);
            if (!exists) {
                order.Add(key);
            }
            cur += CapWeight(m);
        }

        var rows = order
            .OrderBy(k => k.Bucket, StringComparer.Ordinal)
            .Select(k => new object?[] { k.Bucket, k.ArtifactId, k.Level, groups[k] })
            .ToList();
        return rows;
    }

    private List<object?[]> WeightedTimePivot() {
        var col2 = QueryBuilder.GroupByColumn(_def.SecondaryGroupBy);
        var groups = new Dictionary<(string Bucket, string Grp, long ArtifactId, long Level), double>();
        var order = new List<(string Bucket, string Grp, long ArtifactId, long Level)>();
        foreach (var (m, d) in WeightedBucketJoin()) {
            var grp = col2.StartsWith("d.", StringComparison.Ordinal) ? ColValueDrop(d, col2) : ColValue(m, col2);
            var key = (BucketLabel(m.StartTimestamp), grp, d.ArtifactId, d.Level);
            ref var cur = ref CollectionsMarshal.GetValueRefOrAddDefault(groups, key, out var exists);
            if (!exists) {
                order.Add(key);
            }
            cur += CapWeight(m);
        }
        var rows = order
            .OrderBy(k => k.Bucket, StringComparer.Ordinal)
            .ThenBy(k => k.Grp, StringComparer.Ordinal)
            .Select(k => new object?[] { k.Bucket, k.Grp, k.ArtifactId, k.Level, groups[k] })
            .ToList();
        return rows;
    }


    private IEnumerable<string> GroupKeys1D(string col, bool joinDrops) {
        if (joinDrops || col.StartsWith("d.", StringComparison.Ordinal)) {
            foreach (var (m, d) in FilteredJoin()) {
                yield return col.StartsWith("d.", StringComparison.Ordinal) ? ColValueDrop(d, col) : ColValue(m, col);
            }
            yield break;
        }
        foreach (var m in FilteredMissions()) {
            yield return ColValue(m, col);
        }
    }

    private IEnumerable<(string, string)> GroupKeys2D(string col1, string col2, bool joinDrops) {
        var needJoin = joinDrops
            || col1.StartsWith("d.", StringComparison.Ordinal)
            || col2.StartsWith("d.", StringComparison.Ordinal);
        if (needJoin) {
            foreach (var (m, d) in FilteredJoin()) {
                var k1 = col1.StartsWith("d.", StringComparison.Ordinal) ? ColValueDrop(d, col1) : ColValue(m, col1);
                var k2 = col2.StartsWith("d.", StringComparison.Ordinal) ? ColValueDrop(d, col2) : ColValue(m, col2);
                yield return (k1, k2);
            }
            yield break;
        }
        foreach (var m in FilteredMissions()) {
            yield return (ColValue(m, col1), ColValue(m, col2));
        }
    }


    private IEnumerable<MissionRowData> FilteredBucketRows(bool joinDrops) =>
        joinDrops
            ? FilteredJoin().Where(p => InCustomWindow(p.M)).Select(p => p.M)
            : FilteredMissions().Where(InCustomWindow);

    private IEnumerable<(MissionRowData M, ArtifactDropRowData? D)> FilteredBucketJoin(string col2, bool joinDrops) {
        var needJoin = joinDrops || col2.StartsWith("d.", StringComparison.Ordinal);
        if (needJoin) {
            foreach (var (m, d) in FilteredJoin()) {
                if (InCustomWindow(m)) {
                    yield return (m, d);
                }
            }
            yield break;
        }
        foreach (var m in FilteredMissions().Where(InCustomWindow)) {
            yield return (m, null);
        }
    }


    private IEnumerable<(MissionRowData M, ArtifactDropRowData D)> WeightedJoin() {
        var family = new HashSet<long>(_weights.FamilyAfxIds(_def.FamilyWeight).Select(i => (long)i));
        foreach (var (m, d) in FilteredJoin()) {
            if (family.Contains(d.ArtifactId)) {
                yield return (m, d);
            }
        }
    }

    private IEnumerable<(MissionRowData M, ArtifactDropRowData D)> WeightedBucketJoin() =>
        WeightedJoin().Where(p => InCustomWindow(p.M));

    private static double CapWeight(MissionRowData m) =>
        m.NominalCapacity > 0 && m.Capacity > 0
            ? (double)m.NominalCapacity / m.Capacity
            : 1.0;

    private static Comparer<(string, string)> KeyPairComparer(string col1, string col2) =>
        Comparer<(string A, string B)>.Create((x, y) => {
            var c = CompareCol(col1, x.A, y.A);
            return c != 0 ? c : CompareCol(col2, x.B, y.B);
        });


    private static int CompareCol(string col, string a, string b) =>
        !col.EndsWith("spec_type", StringComparison.Ordinal)
            && long.TryParse(a, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var ia)
            && long.TryParse(b, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var ib)
            ? ia.CompareTo(ib)
            : string.CompareOrdinal(a, b);

    private string BucketLabel(long unixSeconds) =>
        TimeBucket.Format(_def.TimeBucket, _def.CustomBucketUnit, unixSeconds);

    private bool InCustomWindow(MissionRowData m) {
        if (_def.TimeBucket != "custom") {
            return true;
        }
        var (cond, modifier) = QueryBuilder.CustomWindowCondition(_def.CustomBucketN, _def.CustomBucketUnit);
        if (cond == "" || modifier is not string mod) {
            return true;
        }
        var cutoff = TimeBucket.NowMinus(mod, _time);
        return m.StartTimestamp >= cutoff;
    }

    private static string ColValue(MissionRowData m, string col) => col switch {
        "m.ship" => m.Ship.ToString(CultureInfo.InvariantCulture),
        "m.duration_type" => m.DurationType.ToString(CultureInfo.InvariantCulture),
        "m.level" => m.Level.ToString(CultureInfo.InvariantCulture),
        "m.mission_type" => m.MissionType.ToString(CultureInfo.InvariantCulture),
        "m.target" => m.Target.ToString(CultureInfo.InvariantCulture),
        _ => "",
    };

    private static string ColValueDrop(ArtifactDropRowData d, string col) => col switch {
        "d.artifact_id" => d.ArtifactId.ToString(CultureInfo.InvariantCulture),
        "d.rarity" => d.Rarity.ToString(CultureInfo.InvariantCulture),
        "d.level" => d.Level.ToString(CultureInfo.InvariantCulture),
        "d.spec_type" => d.SpecType,
        _ => "",
    };
}
