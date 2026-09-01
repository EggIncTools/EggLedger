using System.Globalization;
using EggLedger.Domain.MissionPacking;
using EggLedger.Domain.MissionQuery;
using EggLedger.Web.Missions.Model;

namespace EggLedger.Web.Missions;

public delegate Task<IReadOnlyList<MissionDrop>?> ShipDropsFetcher(string accountId, string missionId);

public sealed class MissionFilterMatcher {
    private readonly string _accountId;
    private readonly ShipDropsFetcher _fetchDrops;
    private readonly TimeZoneInfo _timeZone;


    private readonly Dictionary<int, PossibleMission> _shipConfigs;
    private readonly Dictionary<int, Dictionary<int, DurationConfig>> _durByShip;
    private readonly FilterEvaluator<DatabaseMission, FilterField> _evaluator;

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
            var durs = new Dictionary<int, DurationConfig>();
            foreach (var d in pm.Durations) {
                durs[Convert.ToInt32(d.DurationType, CultureInfo.InvariantCulture)] = d;
            }
            _durByShip[ship] = durs;
        }

        _evaluator = new FilterEvaluator<DatabaseMission, FilterField>()
            .RegisterEnum(FilterField.Ship, m => EnumCode(m.Ship))
            .RegisterEnum(FilterField.DurationType, m => EnumCode(m.DurationType))
            .RegisterEnum(FilterField.MissionType, m => m.MissionType)
            .RegisterEnum(FilterField.Target, m => m.TargetInt)
            .RegisterNumber(FilterField.Level, m => m.Level)
            .RegisterNumber(FilterField.Capacity, m => m.Capacity)
            .RegisterDay(FilterField.LaunchDate, m => DateOnly.FromDateTime(LedgerDate(m.LaunchDT, _timeZone)))
            .RegisterDay(FilterField.ReturnDate, m => DateOnly.FromDateTime(LedgerDate(m.ReturnDT, _timeZone)))
            .RegisterFlag(FilterField.DubCap, m => m.IsDubCap)
            .RegisterFlag(FilterField.BuggedCap, m => m.IsBuggedCap)
            .RegisterAsync(FilterField.Drops, (m, c) => MatchesDropAsync(m, c.Operator, DropOf(c.Value)));
    }

    public static DateTime LedgerDate(long timestampSeconds, TimeZoneInfo timeZone) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTimeOffset.FromUnixTimeSeconds(timestampSeconds).UtcDateTime, timeZone);


    public Task<bool> MatchesAsync(DatabaseMission mission, MissionFilter filter) =>
        _evaluator.MatchesAsync(mission, filter);

    public Task<bool> MatchesAsync(DatabaseMission mission, Condition condition) =>
        _evaluator.MatchesAsync(mission, new MissionFilter([new FilterGroup([condition])]));

    private static DropMatch DropOf(FilterValue v) =>
        v is DropFilterValue d ? d.Match : DropMatch.Any;

    private static int? EnumCode<T>(T? e) where T : struct, Enum =>
        e is null ? null : Convert.ToInt32(e.Value, CultureInfo.InvariantCulture);

    private static bool DropSatisfies(DropMatch m, MissionDrop drop) {
        if (m.Name is { } name && name != drop.Id) {
            return false;
        }
        if (m.Level is { } level && level != drop.Level) {
            return false;
        }
        if (m.Rarity is { } rarity && rarity != drop.Rarity) {
            return false;
        }
        return true;
    }

    private async Task<bool> MatchesDropAsync(DatabaseMission mission, FilterOperator op, DropMatch m) {
        var count = await MatchingDropCountOrNullAsync(mission, m).ConfigureAwait(false);
        if (count is null) {
            return false;
        }

        bool anySatisfies = count > 0;
        return op == FilterOperator.NotContains ? !anySatisfies : anySatisfies;
    }

    public async Task<int> CountMatchingDropsAsync(DatabaseMission mission, DropMatch m) =>
        await MatchingDropCountOrNullAsync(mission, m).ConfigureAwait(false) ?? 0;

    private async Task<int?> MatchingDropCountOrNullAsync(DatabaseMission mission, DropMatch m) {
        var shipConfig = mission.Ship is { } shipEnum
            ? FindShipConfig(Convert.ToInt32(shipEnum, CultureInfo.InvariantCulture))
            : null;
        if (shipConfig is null) {
            return null;
        }
        var durConfig = mission.DurationType is { } durEnum
            ? FindDurConfig(shipConfig, Convert.ToInt32(durEnum, CultureInfo.InvariantCulture))
            : null;
        if (durConfig is null) {
            return null;
        }

        if (m.Quality is { } q) {
            double maxQual = durConfig.MaxQuality + durConfig.LevelQualityBump * mission.Level;
            if (q > maxQual || durConfig.MinQuality > q) {
                return 0;
            }
        }

        var allDrops = await _fetchDrops(_accountId, mission.MissiondId).ConfigureAwait(false);
        if (allDrops is null) {
            return null;
        }

        int count = 0;
        foreach (var drop in allDrops) {
            if (DropSatisfies(m, drop)) {
                count++;
            }
        }
        return count;
    }

    private PossibleMission? FindShipConfig(int ship) =>
        _shipConfigs.GetValueOrDefault(ship);

    private DurationConfig? FindDurConfig(PossibleMission ship, int duration) {
        int shipKey = Convert.ToInt32(ship.Ship, CultureInfo.InvariantCulture);
        return _durByShip.TryGetValue(shipKey, out var durs) && durs.TryGetValue(duration, out var d) ? d : null;
    }


    public async Task<bool> TestMissionAgainstFilterAsync(DatabaseMission mission, FilterCondition filter) {

        if (string.IsNullOrEmpty(filter.TopLevel) || string.IsNullOrEmpty(filter.Op)) {
            return false;
        }
        var typed = FilterCodec.FromLegacyCondition(filter);
        if (typed is null) {

            return true;
        }
        return await MatchesAsync(mission, typed).ConfigureAwait(false);
    }


    public async Task<bool> MissionMatchesFilterAsync(
        DatabaseMission mission,
        IReadOnlyList<FilterCondition> filters,
        IReadOnlyList<IReadOnlyList<FilterCondition>?> orFilters) {
        for (var i = 0; i < filters.Count; i++) {
            if (FilterCodec.FromLegacyCondition(filters[i]) is not { } condition)
                continue;
            if (await MatchesAsync(mission, condition).ConfigureAwait(false))
                continue;

            var siblings = i < orFilters.Count ? orFilters[i] : null;
            if (!await AnySiblingMatchesAsync(mission, siblings).ConfigureAwait(false))
                return false;
        }
        return true;
    }

    private async Task<bool> AnySiblingMatchesAsync(DatabaseMission mission, IReadOnlyList<FilterCondition>? siblings) {
        if (siblings is null)
            return false;
        foreach (var sibling in siblings) {
            if (FilterCodec.FromLegacyCondition(sibling) is { } typed
                && await MatchesAsync(mission, typed).ConfigureAwait(false)) {
                return true;
            }
        }
        return false;
    }
}
