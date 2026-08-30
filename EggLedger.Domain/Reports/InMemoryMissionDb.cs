using System.Globalization;
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


    private readonly ILookup<(string Player, string Mission), ArtifactDropRowData> _dropsByMission;
    private readonly ILookup<(string Player, string Mission), FuelRowData> _fuelByMission;

    private readonly MissionRowPredicate _predicate;

    public InMemoryMissionDb(
        ReportDefinition def,
        IReadOnlyList<MissionRowData> missions,
        IReadOnlyList<ArtifactDropRowData> drops,
        IReadOnlyList<FuelRowData> fuel,
        IWeightData weights) {
        _def = def;
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
            if (_def.SecondaryGroupBy != "") {
                return WeightedPivot();
            }
            return WeightedAggregate();
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
            if (!counts.ContainsKey(key)) {
                order.Add(key);
            }
            counts.TryGetValue(key, out var cur);
            counts[key] = cur + 1;
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
            if (!sums.ContainsKey(key)) {
                order.Add(key);
            }
            sums.TryGetValue(key, out var cur);
            sums[key] = cur + amount;
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
        var counts = new Dictionary<(string, string), long>();
        var order = new List<(string, string)>();
        foreach (var (k1, k2) in GroupKeys2D(col1, col2, joinDrops)) {
            var key = (k1, k2);
            if (!counts.ContainsKey(key)) {
                order.Add(key);
            }
            counts.TryGetValue(key, out var cur);
            counts[key] = cur + 1;
        }

        var rows = order
            .OrderBy(k => k, KeyPairComparer(col1, col2))
            .Select(k => new object?[] { k.Item1, k.Item2, counts[k] })
            .ToList();
        return rows;
    }



    private List<object?[]> Airtime1D(bool joinDrops) {
        var col = QueryBuilder.GroupByColumn(_def.GroupBy);
        var sums = new Dictionary<string, double>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var m in AirtimeRows(joinDrops, col, null)) {
            var key = ColValue(m, col);
            if (!sums.ContainsKey(key)) {
                order.Add(key);
            }
            sums.TryGetValue(key, out var cur);
            sums[key] = cur + ((m.ReturnTimestamp - m.StartTimestamp) / 3600.0);
        }
        return [.. order.Select(k => new object?[] { k, sums[k] })];
    }

    private List<object?[]> Airtime2D(bool joinDrops) {
        var col1 = QueryBuilder.GroupByColumn(_def.GroupBy);
        var col2 = QueryBuilder.GroupByColumn(_def.SecondaryGroupBy);
        var sums = new Dictionary<(string, string), double>();
        var order = new List<(string, string)>();
        foreach (var m in AirtimeRows(joinDrops, col1, col2)) {
            var key = (ColValue(m, col1), ColValue(m, col2));
            if (!sums.ContainsKey(key)) {
                order.Add(key);
            }
            sums.TryGetValue(key, out var cur);
            sums[key] = cur + ((m.ReturnTimestamp - m.StartTimestamp) / 3600.0);
        }
        return [.. order.Select(k => new object?[] { k.Item1, k.Item2, sums[k] })];
    }



    private IEnumerable<MissionRowData> AirtimeRows(bool joinDrops, string col1, string? col2) {
        if (joinDrops
            || col1.StartsWith("d.", StringComparison.Ordinal)
            || (col2 != null && col2.StartsWith("d.", StringComparison.Ordinal))) {
            return FilteredJoin().Select(p => p.M);
        }
        return FilteredMissions();
    }

    private List<object?[]> TimeSeriesCount(bool joinDrops) {
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var m in FilteredBucketRows(joinDrops)) {
            var bucket = BucketLabel(m.StartTimestamp);
            counts.TryGetValue(bucket, out var cur);
            counts[bucket] = cur + 1;
        }
        return [.. counts
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new object?[] { kv.Key, kv.Value })];
    }

    private List<object?[]> TimePivotCount(bool joinDrops) {
        var col2 = QueryBuilder.GroupByColumn(_def.SecondaryGroupBy);
        var counts = new Dictionary<(string, string), long>();
        foreach (var (m, d) in FilteredBucketJoin(col2, joinDrops)) {
            var bucket = BucketLabel(m.StartTimestamp);
            var grp = col2.StartsWith("d.", StringComparison.Ordinal) ? ColValueDrop(d!, col2) : ColValue(m, col2);
            var key = (bucket, grp);
            counts.TryGetValue(key, out var cur);
            counts[key] = cur + 1;
        }
        return [.. counts
            .OrderBy(kv => kv.Key.Item1, StringComparer.Ordinal)
            .ThenBy(kv => kv.Key.Item2, StringComparer.Ordinal)
            .Select(kv => new object?[] { kv.Key.Item1, kv.Key.Item2, kv.Value })];
    }

    private List<object?[]> WeightedAggregate() {
        var col = QueryBuilder.GroupByColumn(_def.GroupBy);
        var groups = new Dictionary<(string, long, long), double>();
        var order = new List<(string, long, long)>();
        foreach (var (m, d) in WeightedJoin()) {
            var key = (col.StartsWith("d.", StringComparison.Ordinal) ? ColValueDrop(d, col) : ColValue(m, col), d.ArtifactId, d.Level);
            if (!groups.ContainsKey(key)) {
                order.Add(key);
            }
            groups.TryGetValue(key, out var cur);
            groups[key] = cur + CapWeight(m);
        }
        var rows = order
            .Select(k => (k, cap: groups[k]))
            .OrderByDescending(x => x.cap)
            .Select(x => new object?[] { x.k.Item1, x.k.Item2, x.k.Item3, x.cap })
            .ToList();
        return rows;
    }

    private List<object?[]> WeightedPivot() {
        var col1 = QueryBuilder.GroupByColumn(_def.GroupBy);
        var col2 = QueryBuilder.GroupByColumn(_def.SecondaryGroupBy);
        var groups = new Dictionary<(string, string, long, long), double>();
        var order = new List<(string, string, long, long)>();
        foreach (var (m, d) in WeightedJoin()) {
            var k1 = col1.StartsWith("d.", StringComparison.Ordinal) ? ColValueDrop(d, col1) : ColValue(m, col1);
            var k2 = col2.StartsWith("d.", StringComparison.Ordinal) ? ColValueDrop(d, col2) : ColValue(m, col2);
            var key = (k1, k2, d.ArtifactId, d.Level);
            if (!groups.ContainsKey(key)) {
                order.Add(key);
            }
            groups.TryGetValue(key, out var cur);
            groups[key] = cur + CapWeight(m);
        }

        var rows = order
            .Select(k => new object?[] { k.Item1, k.Item2, k.Item3, k.Item4, groups[k] })
            .ToList();
        return rows;
    }

    private List<object?[]> WeightedTimeSeries() {
        var groups = new Dictionary<(string, long, long), double>();
        var order = new List<(string, long, long)>();
        foreach (var (m, d) in WeightedBucketJoin()) {
            var key = (BucketLabel(m.StartTimestamp), d.ArtifactId, d.Level);
            if (!groups.ContainsKey(key)) {
                order.Add(key);
            }
            groups.TryGetValue(key, out var cur);
            groups[key] = cur + CapWeight(m);
        }

        var rows = order
            .OrderBy(k => k.Item1, StringComparer.Ordinal)
            .Select(k => new object?[] { k.Item1, k.Item2, k.Item3, groups[k] })
            .ToList();
        return rows;
    }

    private List<object?[]> WeightedTimePivot() {
        var col2 = QueryBuilder.GroupByColumn(_def.SecondaryGroupBy);
        var groups = new Dictionary<(string, string, long, long), double>();
        var order = new List<(string, string, long, long)>();
        foreach (var (m, d) in WeightedBucketJoin()) {
            var grp = col2.StartsWith("d.", StringComparison.Ordinal) ? ColValueDrop(d, col2) : ColValue(m, col2);
            var key = (BucketLabel(m.StartTimestamp), grp, d.ArtifactId, d.Level);
            if (!groups.ContainsKey(key)) {
                order.Add(key);
            }
            groups.TryGetValue(key, out var cur);
            groups[key] = cur + CapWeight(m);
        }
        var rows = order
            .OrderBy(k => k.Item1, StringComparer.Ordinal)
            .ThenBy(k => k.Item2, StringComparer.Ordinal)
            .Select(k => new object?[] { k.Item1, k.Item2, k.Item3, k.Item4, groups[k] })
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


    private IEnumerable<MissionRowData> FilteredBucketRows(bool joinDrops) {
        if (joinDrops) {
            return FilteredJoin().Where(p => InCustomWindow(p.M)).Select(p => p.M);
        }
        return FilteredMissions().Where(InCustomWindow);
    }

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


    private static int CompareCol(string col, string a, string b) {
        if (!col.EndsWith("spec_type", StringComparison.Ordinal)
            && long.TryParse(a, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var ia)
            && long.TryParse(b, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var ib)) {
            return ia.CompareTo(ib);
        }
        return string.CompareOrdinal(a, b);
    }

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
        var cutoff = TimeBucket.NowMinus(mod);
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
