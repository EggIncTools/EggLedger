using System.Diagnostics;
using System.Text.Json;
using EggLedger.Domain.MissionPacking;
using EggLedger.Domain.MissionQuery;
using EggLedger.Web.Components;
using EggLedger.Web.Data;
using EggLedger.Web.Missions;
using EggLedger.Web.Missions.Model;
using EggLedger.Web.Platform;
using Microsoft.AspNetCore.Components;

namespace EggLedger.Web.State;

public enum ShipsViewMode {
    List,
    Calendar
}

public interface IShipsViewActions {
    Task ResortAsync(MissionSortMethod method);
}

public sealed class ShipsViewState(
    MissionConfigProvider config,
    ActiveAccount active,
    IndexedDbSettings settings,
    LedgerDataHub hub,
    IUserTimeZoneProvider timeZones) : IDisposable {
    private static readonly JsonSerializerOptions CardJsonOptions = new(JsonSerializerDefaults.Web);

    private Func<Func<Task>, Task>? _dispatch;
    private Dictionary<string, List<MissionDrop>>? _allDrops;
    private MissionFilterMatcher? _matcher;
    private bool _initialized;
    private int _loadGeneration;
    private int _filterGeneration;

    public event Action? Changed;

    public MissionViewOptions Opts { get; } = new();

    public HashSet<string> MultiSelectedIds { get; } = [];

    public ShipsViewMode ViewMode { get; private set; }

    public bool Loading { get; private set; } = true;

    public bool Applying { get; private set; }

    public string ResultText { get; private set; } = "";

    public string? MissionBeingViewed { get; private set; }

    public IReadOnlyList<DatabaseMission>? AllMissions { get; private set; }

    public IReadOnlyList<DatabaseMission>? Filtered { get; private set; }

    public IReadOnlyList<DatabaseMission>? TabFiltered { get; private set; }

    public IReadOnlyList<DatabaseMission>? TabFilteredAll { get; private set; }

    public bool HasActiveFilter { get; private set; }

    public IReadOnlyList<DatabaseMission> InFlight { get; private set; } = [];

    public IReadOnlyList<DatabaseMission>? FlatSorted { get; private set; }

    public MissionGrouping? Grouping { get; private set; }

    public bool HasBothTypes { get; private set; }

    public CardPresetSet CardPresets { get; private set; } = CardPresetSet.Default;

    public Dictionary<string, int> DropCounts { get; private set; } = [];

    public IShipsViewActions? Actions { get; set; }

    public string? AccountId => active.ActiveAccountId;

    public FilterFieldCtx FieldCtx => config.FieldCtx;

    public IReadOnlyList<DatabaseMission> TabFilteredInFlight =>
        MissionViewOptions.TabFilteredMissions(InFlight, Opts.MissionTypeTab) ?? [];

    public async Task EnsureInitializedAsync(Func<Func<Task>, Task> dispatch) {
        if (_initialized) {
            return;
        }

        _initialized = true;
        _dispatch = dispatch;
        active.Changed += OnAccountChanged;
        hub.AccountInvalidated += OnAccountInvalidated;
        var stored = await settings.GetAllSettingsAsync();
        Opts.LoadFrom(stored);
        CardPresets = stored.TryGetValue(MissionViewOptions.KeyCardPresets, out var json) && !string.IsNullOrEmpty(json)
            ? JsonSerializer.Deserialize<CardPresetSet>(json, CardJsonOptions) ?? CardPresetSet.Default
            : CardPresetSet.Default;
        await LoadAsync();
    }

    public async Task LoadAsync() {
        var generation = ++_loadGeneration;
        var filterGeneration = ++_filterGeneration;
        Applying = false;
        MissionBeingViewed = null;
        var id = active.ActiveAccountId;
        if (id is null) {
            AllMissions = null;
            Filtered = null;
            Grouping = null;
            InFlight = [];
            Changed?.Invoke();
            return;
        }

        ResultText = "";
        _allDrops = null;
        DropCounts = [];
        FlatSorted = null;
        HasActiveFilter = false;

        var pending = hub.GetMissionsAsync(id);
        var pendingInFlight = hub.GetInFlightAsync(id);
        if (!pending.IsCompleted) {
            Loading = true;
            Changed?.Invoke();
        }

        IReadOnlyList<DatabaseMission> missions = await pending ?? [];
        IReadOnlyList<DatabaseMission> inFlight = await pendingInFlight ?? [];
        if (generation != _loadGeneration) {
            return;
        }

        AllMissions = missions;
        InFlight = inFlight;
        HasBothTypes = MissionViewOptions.HasBothMissionTypes(missions);
        if (!HasBothTypes) {
            Opts.MissionTypeTab = null;
        }

        _matcher = new MissionFilterMatcher(
            config.DurationConfigs,
            id,
            DropsFetcher,
            timeZones.TimeZone);

        if (filterGeneration == _filterGeneration) {
            Filtered = missions;
        }

        Regroup();
        Loading = false;
        Changed?.Invoke();
    }

    public async Task ApplyFilterAsync(MissionFilterBar.LegacyFilter filter) {
        if (AllMissions is not { } missions || _matcher is not { } matcher || active.ActiveAccountId is not { } id) {
            return;
        }

        var generation = ++_filterGeneration;
        Applying = true;
        DropCounts = [];
        FlatSorted = null;
        Changed?.Invoke();

        var countableDrop = CountableDrop(filter);
        var sw = Stopwatch.StartNew();
        try {
            var matchedIds = await hub.GetFilterMatchesAsync(
                id,
                FilterMatching.Hash(timeZones.TimeZone.Id, filter.And, filter.Or),
                () => FilterMatching.MatchingIdsAsync(matcher, missions, filter.And, filter.Or));

            var (result, dropCounts) = await ProjectMatchesAsync(matcher, missions, matchedIds, countableDrop);
            sw.Stop();
            if (generation != _filterGeneration) {
                return;
            }

            DropCounts = dropCounts;
            Filtered = result;
            HasActiveFilter = HasConditions(filter);
            PruneSelection();
            var shown = result.Count;
            var filteredOut = missions.Count - shown;
            ResultText = $"Filtered in {sw.Elapsed.TotalSeconds:0.###}s ({shown} shown, {filteredOut} filtered out)";
            Regroup();
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

    private static bool HasConditions(MissionFilterBar.LegacyFilter filter) {
        return filter.And.Count > 0 || filter.Or.Any(g => g is { Count: > 0 });
    }

    private static DropMatch? CountableDrop(MissionFilterBar.LegacyFilter filter) {
        foreach (var fc in filter.And) {
            var typed = FilterCodec.FromLegacyCondition(fc);
            if (typed is { Field: FilterField.Drops, Operator: FilterOperator.Contains, Value: DropFilterValue d }) {
                return d.Match;
            }
        }

        return null;
    }

    private static async Task<(List<DatabaseMission> Result, Dictionary<string, int> Counts)> ProjectMatchesAsync(
        MissionFilterMatcher matcher,
        IReadOnlyList<DatabaseMission> missions,
        IReadOnlySet<string> matchedIds,
        DropMatch? countableDrop) {
        var result = new List<DatabaseMission>();
        var counts = new Dictionary<string, int>();
        foreach (var mission in missions) {
            if (!matchedIds.Contains(mission.MissiondId)) {
                continue;
            }

            result.Add(mission);
            if (countableDrop is null) {
                continue;
            }

            var count = await matcher.CountMatchingDropsAsync(mission, countableDrop);
            if (count > 0) {
                counts[mission.MissiondId] = count;
            }
        }

        return (result, counts);
    }

    public async Task SaveCardPresetSetAsync(CardPresetSet updated) {
        CardPresets = updated;
        var json = JsonSerializer.Serialize(CardPresets, CardJsonOptions);
        await settings.SetSettingAsync(MissionViewOptions.KeyCardPresets, json);
        Changed?.Invoke();
    }

    public void SetViewMode(ShipsViewMode mode) {
        ViewMode = mode;
        Changed?.Invoke();
    }

    public void SelectTab(int? tab) {
        Opts.MissionTypeTab = tab;
        Regroup();
        Changed?.Invoke();
    }

    public void SetMissionBeingViewed(string? missionId) {
        MissionBeingViewed = missionId;
        Changed?.Invoke();
    }

    public int CountOfType(int type) {
        if (Filtered is null) {
            return 0;
        }

        var n = 0;
        foreach (var m in Filtered) {
            if (m.MissionType == type) {
                n++;
            }
        }

        return n;
    }

    public async Task SetOptAsync(string key, Action<bool> apply, ChangeEventArgs e) {
        var value = e.Value is bool b ? b : bool.TryParse(e.Value?.ToString(), out var parsed) && parsed;
        apply(value);
        await settings.SetSettingAsync(key, value ? "true" : "false");
        Changed?.Invoke();
    }

    public async Task SetMultiViewAsync(MultiViewMode mode) {
        Opts.MultiViewMode = mode;
        MultiSelectedIds.Clear();
        await settings.SetSettingAsync(MissionViewOptions.KeyMultiViewMode, MissionViewOptions.MultiViewModeToString(mode));
        Changed?.Invoke();
    }

    public async Task ToggleSortByDropCountAsync(ChangeEventArgs e) {
        await SetOptAsync(MissionViewOptions.KeySortByDropCount, b => Opts.SortByDropCount = b, e);
        if (Opts.SortByDropCount && Opts.MultiViewMode == MultiViewMode.Row) {
            await SetMultiViewAsync(MultiViewMode.Off);
        }

        Regroup();
        Changed?.Invoke();
    }

    public async Task SetSortMethodAsync(MissionSortMethod method) {
        Opts.SortMethod = method;
        await settings.SetSettingAsync(MissionViewOptions.KeySortMethod, MissionViewOptions.SortMethodToString(method));
        if (Actions is { } actions) {
            await actions.ResortAsync(method);
        }

        Changed?.Invoke();
    }

    public void Dispose() {
        active.Changed -= OnAccountChanged;
        hub.AccountInvalidated -= OnAccountInvalidated;
    }

    private void OnAccountChanged() {
        _ = _dispatch?.Invoke(LoadAsync);
    }

    private void OnAccountInvalidated(string accountId) {
        if (accountId == active.ActiveAccountId) {
            _ = _dispatch?.Invoke(LoadAsync);
        }
    }

    private async Task<IReadOnlyList<MissionDrop>?> DropsFetcher(string accountId, string missionId) {
        _allDrops ??= await hub.GetDropsAsync(accountId) ?? [];
        return _allDrops.TryGetValue(missionId, out var drops) ? drops : [];
    }

    private void Regroup() {
        TabFiltered = MissionViewOptions.TabFilteredMissions(Filtered, Opts.MissionTypeTab);
        TabFilteredAll = MissionViewOptions.TabFilteredMissions(AllMissions, Opts.MissionTypeTab);
        Grouping = MissionGrouper.Group(TabFiltered, ts => MissionFilterMatcher.LedgerDate(ts, timeZones.TimeZone), true);

        if (Opts.SortByDropCount && DropCounts.Count > 0 && TabFiltered is { Count: > 0 }) {
            var sorted = new List<DatabaseMission>(TabFiltered);
            sorted.Sort((a, b) => {
                DropCounts.TryGetValue(a.MissiondId, out var ca);
                DropCounts.TryGetValue(b.MissiondId, out var cb);
                var cmp = cb.CompareTo(ca);
                if (cmp != 0) {
                    return cmp;
                }

                return b.LaunchDT.CompareTo(a.LaunchDT);
            });
            FlatSorted = sorted;
        } else {
            FlatSorted = null;
        }
    }

    private void PruneSelection() {
        if (Filtered is null) {
            MultiSelectedIds.Clear();
            return;
        }

        var valid = new HashSet<string>();
        foreach (var m in Filtered) {
            valid.Add(m.MissiondId);
        }

        MultiSelectedIds.RemoveWhere(id => !valid.Contains(id));
    }
}
