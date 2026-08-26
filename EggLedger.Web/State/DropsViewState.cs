using System.Diagnostics;
using EggLedger.Domain.MissionPacking;
using EggLedger.Domain.MissionQuery;
using EggLedger.Web.Components;
using EggLedger.Web.Data;
using EggLedger.Web.Missions;
using EggLedger.Web.Missions.Model;
using EggLedger.Web.Platform;
using EggLedger.Web.Services;
using EggLedger.Web.Settings;
using Microsoft.AspNetCore.Components;

namespace EggLedger.Web.State;

public sealed class DropsViewState(
    MissionConfigProvider config,
    ActiveAccount active,
    IndexedDbSettings settings,
    LedgerDataHub hub,
    IUserTimeZoneProvider timeZones,
    MennoService menno) : IDisposable {
    public const string KeySortMethod = "lifetime_sort_method";
    public const string KeyShowPerShip = "lifetime_show_drops_per_ship";
    public const string KeyShowExpectedTotals = "lifetime_show_expected_totals";

    private readonly PersistedSetting<LifetimeSortMethod> _sortMethod = new(
        settings, KeySortMethod, LifetimeSortMethod.Default, LifetimeSorter.ParseMethod, LifetimeSorter.MethodString);

    private readonly PersistedSetting<bool> _showPerShip = PersistedSetting.Bool(settings, KeyShowPerShip);

    private readonly PersistedSetting<bool> _showExpectedTotals = PersistedSetting.Bool(settings, KeyShowExpectedTotals);

    private Func<Func<Task>, Task>? _dispatch;
    private IReadOnlyList<DatabaseMission>? _matchedMissions;
    private MissionFilterMatcher? _matcher;
    private IReadOnlyList<DatabaseMission>? _allMissions;
    private Dictionary<string, List<MissionDrop>>? _allDropsCache;
    private bool _initialized;
    private int _loadGeneration;
    private int _filterGeneration;

    public event Action? Changed;

    public bool Loading { get; private set; } = true;

    public LifetimeData? Data { get; private set; }

    public LifetimeSortMethod SortMethod => _sortMethod.Value;

    public bool Applying { get; private set; }

    public string ResultText { get; private set; } = "";

    public bool ShowPerShip => _showPerShip.Value;

    public bool ShowExpectedTotals => _showExpectedTotals.Value;

    public bool AllowExpectedTotals { get; private set; }

    public MissionMennoData? MennoData { get; private set; }

    public string? AccountId => active.ActiveAccountId;

    public FilterFieldCtx FieldCtx => config.FieldCtx;

    public async Task EnsureInitializedAsync(Func<Func<Task>, Task> dispatch) {
        if (_initialized) {
            return;
        }

        _initialized = true;
        _dispatch = dispatch;
        active.Changed += OnAccountChanged;
        hub.AccountInvalidated += OnAccountInvalidated;
        await _sortMethod.LoadAsync();
        await _showPerShip.LoadAsync();
        await _showExpectedTotals.LoadAsync();
        await LoadAsync();
    }

    public async Task LoadAsync() {
        var generation = ++_loadGeneration;
        var filterGeneration = ++_filterGeneration;
        Applying = false;
        ResultText = "";
        var id = active.ActiveAccountId;
        if (id is null) {
            Data = null;
            Changed?.Invoke();
            return;
        }

        var pending = hub.GetDropsAsync(id);
        if (!pending.IsCompleted) {
            Loading = true;
            Changed?.Invoke();
        }

        var dropsByMission = await pending;
        if (generation != _loadGeneration) {
            return;
        }

        Loading = false;

        if (dropsByMission is null) {
            if (filterGeneration == _filterGeneration) {
                Data = null;
            }

            Changed?.Invoke();
            return;
        }

        var missions = await hub.GetMissionsAsync(id) ?? [];
        var data = await hub.GetLifetimeAggregateAsync(id);
        if (generation != _loadGeneration) {
            return;
        }

        if (data is null) {
            if (filterGeneration == _filterGeneration) {
                Data = null;
            }

            Changed?.Invoke();
            return;
        }

        _allMissions = missions;
        _allDropsCache = dropsByMission;
        _matcher = new MissionFilterMatcher(
            config.DurationConfigs,
            id,
            (_, missionId) => DropsFetcher(missionId),
            timeZones.TimeZone);

        if (filterGeneration == _filterGeneration) {
            LifetimeSorter.Sort(data, SortMethod);
            Data = data;
            _matchedMissions = _allMissions;
            await RecomputeMennoAsync(
                () => generation == _loadGeneration && filterGeneration == _filterGeneration);
            if (generation != _loadGeneration) {
                return;
            }
        }

        Changed?.Invoke();
    }

    public async Task ApplyFilterAsync(MissionFilterBar.LegacyFilter filter) {
        if (_allMissions is not { } missions
            || _matcher is not { } matcher
            || _allDropsCache is null
            || active.ActiveAccountId is not { } id) {
            return;
        }

        var generation = ++_filterGeneration;
        Applying = true;
        Changed?.Invoke();

        var sw = Stopwatch.StartNew();
        try {
            var matchedIds = await hub.GetFilterMatchesAsync(
                id,
                FilterMatching.Hash(timeZones.TimeZone.Id, filter.And, filter.Or),
                () => FilterMatching.MatchingIdsAsync(matcher, missions, filter.And, filter.Or));

            sw.Stop();
            if (generation != _filterGeneration) {
                return;
            }

            var filteredDrops = new Dictionary<string, List<MissionDrop>>();
            foreach (var kvp in _allDropsCache) {
                if (matchedIds.Contains(kvp.Key)) {
                    filteredDrops[kvp.Key] = kvp.Value;
                }
            }

            var data = LifetimeAggregator.Aggregate(filteredDrops);
            LifetimeSorter.Sort(data, SortMethod);
            Data = data;
            _matchedMissions = missions.Where(m => matchedIds.Contains(m.MissiondId)).ToList();
            await RecomputeMennoAsync(() => generation == _filterGeneration);
            if (generation != _filterGeneration) {
                return;
            }

            var shown = matchedIds.Count;
            var filteredOut = missions.Count - shown;
            ResultText = $"Filtered in {sw.Elapsed.TotalSeconds:0.###}s ({shown} shown, {filteredOut} filtered out)";
            Applying = false;
            Changed?.Invoke();
        } catch (Exception) {
            if (generation != _filterGeneration) {
                return;
            }

            ResultText = FilterMatching.FailedText;
            Applying = false;
            Changed?.Invoke();
        }
    }

    public async Task ToggleShowPerShipAsync(ChangeEventArgs e) {
        await _showPerShip.SetAsync(e.Value is true);
        Changed?.Invoke();
    }

    public async Task ToggleExpectedTotalsAsync(ChangeEventArgs e) {
        await _showExpectedTotals.SetAsync(e.Value is true);
        var load = _loadGeneration;
        var filter = _filterGeneration;
        await RecomputeMennoAsync(() => load == _loadGeneration && filter == _filterGeneration);
        Changed?.Invoke();
    }

    public async Task SetSortAsync(LifetimeSortMethod method) {
        await _sortMethod.SetAsync(method);
        if (Data is not null) {
            LifetimeSorter.Sort(Data, SortMethod);
        }

        Changed?.Invoke();
    }

    public void Dispose() {
        active.Changed -= OnAccountChanged;
        hub.AccountInvalidated -= OnAccountInvalidated;
    }

    private static (int Ship, int Duration, int Level, int Target)? SingleConfig(IReadOnlyList<DatabaseMission>? missions) {
        if (missions is null || missions.Count == 0) {
            return null;
        }

        (int, int, int, int)? cfg = null;
        foreach (var m in missions) {
            var p = MissionDetailBuilder.MennoParams(m);
            if (cfg is null) {
                cfg = p;
            } else if (cfg != p) {
                return null;
            }
        }

        return cfg;
    }

    private void OnAccountChanged() {
        _ = _dispatch?.Invoke(LoadAsync);
    }

    private void OnAccountInvalidated(string accountId) {
        if (accountId == active.ActiveAccountId) {
            _ = _dispatch?.Invoke(LoadAsync);
        }
    }

    private async Task RecomputeMennoAsync(Func<bool> isCurrent) {
        var cfg = SingleConfig(_matchedMissions);
        MissionMennoData? mennoData = ShowExpectedTotals && cfg is not null
            ? await LoadMennoAsync(cfg.Value)
            : null;

        if (!isCurrent()) {
            return;
        }

        AllowExpectedTotals = cfg is not null;
        MennoData = mennoData;
    }

    private async Task<MissionMennoData?> LoadMennoAsync((int Ship, int Duration, int Level, int Target) cfg) {
        try {
            await menno.EnsureLoadedAsync();
        } catch (Exception) {
            return null;
        }

        var configs = menno.GetData(cfg.Ship, cfg.Duration, cfg.Level, cfg.Target);
        var total = 0;
        foreach (var c in configs) {
            total += c.TotalDrops;
        }

        return configs.Count > 0 ? new MissionMennoData { Configs = configs, TotalDropsCount = total } : null;
    }

    private Task<IReadOnlyList<MissionDrop>?> DropsFetcher(string missionId) {
        IReadOnlyList<MissionDrop> drops = _allDropsCache is not null && _allDropsCache.TryGetValue(missionId, out var found)
            ? found
            : [];
        return Task.FromResult<IReadOnlyList<MissionDrop>?>(drops);
    }
}
