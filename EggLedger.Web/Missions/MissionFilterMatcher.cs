using System.Globalization;
using EggLedger.Domain.MissionPacking;
using EggLedger.Domain.MissionQuery;
using EggLedger.Domain.Reports;

namespace EggLedger.Web.Missions;

public delegate Task<IReadOnlyList<MissionDrop>?> ShipDropsFetcher(string accountId, string missionId);

public sealed record DropMatch(int? Name, int? Level, int? Rarity, double? Quality = null) {
    public static readonly DropMatch Any = new(null, null, null);

    public static DropMatch Decode(string glob) {
        var segs = (glob ?? "").Split('_');
        return new DropMatch(Seg(segs, 0), Seg(segs, 1), Seg(segs, 2), SegD(segs, 3));

        static int? Seg(string[] s, int i) =>
            i < s.Length && s[i] is not ("%" or "")
                && int.TryParse(s[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

        static double? SegD(string[] s, int i) =>
            i < s.Length && s[i] is not ("%" or "")
                && double.TryParse(s[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    public string Encode() {
        static string Part(object? v) => v is IFormattable f ? f.ToString(null, CultureInfo.InvariantCulture) : "%";
        return $"{Part(Name)}_{Part(Level)}_{Part(Rarity)}_{Part(Quality)}";
    }
}

public sealed class MissionFilterMatcher {
    private static readonly FilterEvaluation Evaluation = new(LedgerFilters.MissionScope, Strict: false);

    private readonly string _accountId;
    private readonly ShipDropsFetcher _fetchDrops;
    private readonly TimeZoneInfo _timeZone;

    private readonly Dictionary<int, PossibleMission> _shipConfigs;
    private readonly Dictionary<int, Dictionary<int, DurationConfig>> _durByShip;

    public MissionFilterMatcher(
        IReadOnlyList<PossibleMission> durationConfigs,
        string? accountId,
        ShipDropsFetcher fetchDrops,
        TimeZoneInfo timeZone) {
        _accountId = accountId ?? "";
        _fetchDrops = fetchDrops;
        _timeZone = timeZone;

        _shipConfigs = [];
        _durByShip = [];
        foreach (var pm in durationConfigs) {
            int ship = Convert.ToInt32(pm.Ship, CultureInfo.InvariantCulture);
            _shipConfigs[ship] = pm;
            _durByShip[ship] = pm.Durations
                .GroupBy(d => Convert.ToInt32(d.DurationType, CultureInfo.InvariantCulture))
                .ToDictionary(g => g.Key, g => g.Last());
        }
    }

    public static DateTime LedgerDate(long timestampSeconds, TimeZoneInfo timeZone) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTimeOffset.FromUnixTimeSeconds(timestampSeconds).UtcDateTime, timeZone);

    public async Task<bool> MatchesAsync(DatabaseMission mission, FilterSet filter) {
        var drops = UsesDrops(filter) ? await DropKeysAsync(mission).ConfigureAwait(false) : null;
        return FilterEvaluator.Evaluate(filter, Fields(mission), key => Subject(mission, key, drops), Evaluation).Matched;
    }

    public Task<bool> MatchesAsync(DatabaseMission mission, FilterCondition condition) =>
        MatchesAsync(mission, new FilterSet([condition], []));

    private static bool UsesDrops(FilterSet filter) =>
        LedgerFilters.Conditions(filter).Any(c => LedgerFilters.Find(c.Field)?.Key == "drops");

    private IReadOnlyList<FilterFieldDef> Fields(DatabaseMission mission) =>
        [.. LedgerFilters.Fields.Select(f => f.Key == "drops" ? f with { Compare = (actual, op, value) => CompareDrops(mission, actual, op, value) } : f)];

    private FilterSubjectValue Subject(DatabaseMission m, string key, IReadOnlyList<string>? drops) => key switch {
        "ship" => FilterSubjectValue.Of(EnumCode(m.Ship)),
        "duration" => FilterSubjectValue.Of(EnumCode(m.DurationType)),
        "level" => FilterSubjectValue.Of((double)m.Level),
        "target" => FilterSubjectValue.Of((double)m.TargetInt),
        "type" => FilterSubjectValue.Of((double)m.MissionType),
        "launchDT" => FilterSubjectValue.Of(LocalDay(m.LaunchDT)),
        "returnDT" => FilterSubjectValue.Of(LocalDay(m.ReturnDT)),
        "dubcap" => FilterSubjectValue.Of(m.IsDubCap),
        "buggedcap" => FilterSubjectValue.Of(m.IsBuggedCap),
        "drops" => drops is null ? FilterSubjectValue.Absent : FilterSubjectValue.Of(drops),
        _ => FilterSubjectValue.Absent,
    };

    private DateTimeOffset LocalDay(long timestampSeconds) =>
        new(DateOnly.FromDateTime(LedgerDate(timestampSeconds, _timeZone)).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    private static double? EnumCode<T>(T? e) where T : struct, Enum =>
        e is null ? null : Convert.ToInt32(e.Value, CultureInfo.InvariantCulture);

    private bool? CompareDrops(DatabaseMission mission, FilterSubjectValue actual, string op, string value) {
        if (actual is not FilterSubjectValue.ManyValue many) return false;
        var match = DropMatch.Decode(value);
        var any = QualityInRange(mission, match) && many.Values.Any(key => LedgerFilters.DropMatchesGlob(key, value));
        return op == FilterOps.NotContains ? !any : any;
    }

    private bool QualityInRange(DatabaseMission mission, DropMatch m) {
        if (m.Quality is not { } q) return true;
        if (DurConfig(mission) is not { } dur) return false;
        double maxQual = dur.MaxQuality + dur.LevelQualityBump * mission.Level;
        return q <= maxQual && dur.MinQuality <= q;
    }

    private async Task<IReadOnlyList<string>?> DropKeysAsync(DatabaseMission mission) {
        if (DurConfig(mission) is null) return null;
        var drops = await _fetchDrops(_accountId, mission.MissiondId).ConfigureAwait(false);
        return drops?.Select(d => LedgerFilters.DropKey(d.Id, d.Level, d.Rarity)).ToList();
    }

    public async Task<int> CountMatchingDropsAsync(DatabaseMission mission, DropMatch m) {
        if (!QualityInRange(mission, m) || await DropKeysAsync(mission).ConfigureAwait(false) is not { } keys) return 0;
        var glob = m.Encode();
        return keys.Count(key => LedgerFilters.DropMatchesGlob(key, glob));
    }

    private DurationConfig? DurConfig(DatabaseMission mission) {
        if (mission.Ship is not { } shipEnum || mission.DurationType is not { } durEnum) return null;
        var ship = Convert.ToInt32(shipEnum, CultureInfo.InvariantCulture);
        var duration = Convert.ToInt32(durEnum, CultureInfo.InvariantCulture);
        return _shipConfigs.ContainsKey(ship) && _durByShip.TryGetValue(ship, out var durs) && durs.TryGetValue(duration, out var d) ? d : null;
    }
}
