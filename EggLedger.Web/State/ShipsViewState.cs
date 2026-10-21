using System.Diagnostics;
using System.Text.Json;
using EggLedger.Domain.MissionPacking;
using EggLedger.Domain.MissionQuery;
using EggLedger.Web.Data;
using EggLedger.Web.Missions;
using EggLedger.Web.Missions.Model;
using EggLedger.Web.Platform;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

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
    IUserTimeZoneProvider timeZones,
    ILogger<ShipsViewState> logger) : IDisposable {
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

    public CardTemplateSet CardTemplates { get; private set; } = CardTemplateSet.Default;

    public IReadOnlyDictionary<string, MissionDropSummary> DropSummaries { get; private set; } = new Dictionary<string, MissionDropSummary>();

    public Dictionary<string, int> DropCounts { get; private set; } = [];

    public IShipsViewActions? Actions { get; set; }

    public IReadOnlyList<MissionCardData> DesignerSamples { get; private set; } = [];

    public string? AccountId => active.ActiveAccountId;

    public FilterFieldCtx FieldCtx => config.FieldCtx;

    public IReadOnlyList<DatabaseMission> TabFilteredInFlight =>
        MissionViewOptions.TabFilteredMissions(InFlight, Opts.MissionTypeTab) ?? [];

    public async Task EnsureInitializedAsync(Func<Func<Task>, Task> dispatch) {
        if (_initialized) return;

        _initialized = true;
        _dispatch = dispatch;
        active.Changed += OnAccountChanged;
        hub.AccountInvalidated += OnAccountInvalidated;
        timeZones.Changed += OnAccountChanged;
        var stored = await settings.GetAllSettingsAsync();
        Opts.LoadFrom(stored);
        if (stored.TryGetValue(MissionViewOptions.KeyCardTemplates, out var templatesJson) && !string.IsNullOrEmpty(templatesJson)) {
            CardTemplates = CardTemplateCodec.Deserialize(templatesJson);
        } else if (stored.TryGetValue(MissionViewOptions.KeyCardPresets, out var legacyJson) && !string.IsNullOrEmpty(legacyJson)) {
            CardPresetSet legacy;
            try {
                legacy = JsonSerializer.Deserialize<CardPresetSet>(legacyJson, CardJsonOptions) ?? CardPresetSet.Default;
            } catch (JsonException ex) {
                logger.LogDebug(ex, "legacy card presets unreadable");
                legacy = CardPresetSet.Default;
            }
            CardTemplates = LegacyCardUpgrade.FromPresetSet(legacy);
            await settings.SetSettingAsync(MissionViewOptions.KeyCardTemplates, CardTemplateCodec.Serialize(CardTemplates));
        } else {
            CardTemplates = CardTemplateSet.Default;
        }

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
        DropSummaries = new Dictionary<string, MissionDropSummary>();
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
        if (generation != _loadGeneration) return;

        AllMissions = missions;
        InFlight = inFlight;
        HasBothTypes = MissionViewOptions.HasBothMissionTypes(missions);
        if (!HasBothTypes) Opts.MissionTypeTab = null;

        _matcher = new MissionFilterMatcher(
            config.DurationConfigs,
            id,
            DropsFetcher,
            timeZones.TimeZone);

        if (filterGeneration == _filterGeneration) Filtered = missions;

        Regroup();
        await EnsureDropSummariesAsync();
        if (generation != _loadGeneration) return;

        Loading = false;
        Changed?.Invoke();
    }

    public async Task ApplyFilterAsync(FilterSet filter) {
        if (AllMissions is not { } missions || _matcher is not { } matcher || active.ActiveAccountId is not { } id) return;

        var generation = ++_filterGeneration;
        Applying = true;
        DropCounts = [];
        FlatSorted = null;
        Changed?.Invoke();

        var countableDrop = FilterMatching.CountableDrop(filter);
        var sw = Stopwatch.StartNew();
        try {
            var matchedIds = await hub.GetFilterMatchesAsync(
                id,
                FilterCodec.Hash(timeZones.TimeZone.Id, filter),
                () => FilterMatching.MatchingIdsAsync(matcher, missions, filter));

            var (result, dropCounts) = await ProjectMatchesAsync(matcher, missions, matchedIds, countableDrop);
            sw.Stop();
            if (generation != _filterGeneration) return;

            DropCounts = dropCounts;
            Filtered = result;
            HasActiveFilter = FilterMatching.HasConditions(filter);
            PruneSelection();
            var shown = result.Count;
            var filteredOut = missions.Count - shown;
            ResultText = $"Filtered in {sw.Elapsed.TotalSeconds:0.###}s ({shown} shown, {filteredOut} filtered out)";
            Regroup();
            Applying = false;
            Changed?.Invoke();
        } catch (Exception ex) {
            logger.LogDebug(ex, "ships filter failed for generation {Generation}", generation);
            if (generation != _filterGeneration) return;

            ResultText = FilterMatching.FailedText;
            Applying = false;
            Changed?.Invoke();
        }
    }

    private static async Task<(List<DatabaseMission> Result, Dictionary<string, int> Counts)> ProjectMatchesAsync(
        MissionFilterMatcher matcher,
        IReadOnlyList<DatabaseMission> missions,
        IReadOnlySet<string> matchedIds,
        DropMatch? countableDrop) {
        var result = new List<DatabaseMission>();
        var counts = new Dictionary<string, int>();
        foreach (var mission in missions) {
            if (!matchedIds.Contains(mission.MissiondId)) continue;

            result.Add(mission);
            if (countableDrop is null) continue;

            var count = await matcher.CountMatchingDropsAsync(mission, countableDrop);
            if (count > 0) counts[mission.MissiondId] = count;
        }

        return (result, counts);
    }

    public async Task SaveCardTemplatesAsync(CardTemplateSet set) {
        CardTemplates = set;
        await settings.SetSettingAsync(MissionViewOptions.KeyCardTemplates, CardTemplateCodec.Serialize(set));
        await EnsureDropSummariesAsync();
        Changed?.Invoke();
    }

    public MissionCardData DataFor(DatabaseMission m) =>
        new(
            m,
            timeZones.TimeZone,
            DropCounts.TryGetValue(m.MissiondId, out var count) ? count : null,
            DropSummaries.TryGetValue(m.MissiondId, out var s) ? s : null,
            null);

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
        if (Filtered is null) return 0;

        var n = 0;
        foreach (var m in Filtered) {
            if (m.MissionType == type) n++;
        }

        return n;
    }

    public async Task SetOptAsync(string key, Action<bool> apply, ChangeEventArgs e) {
        var value = e.Value is bool b ? b : bool.TryParse(e.Value?.ToString(), out var parsed) && parsed;
        apply(value);
        await settings.SetSettingAsync(key, value ? "true" : "false");
        Changed?.Invoke();
    }

    public async Task SetCalendarEventTypeAsync(string type, ChangeEventArgs e) {
        var shown = e.Value is bool b ? b : bool.TryParse(e.Value?.ToString(), out var parsed) && parsed;
        if (shown) {
            Opts.CalendarHiddenEventTypes.Remove(type);
        } else {
            Opts.CalendarHiddenEventTypes.Add(type);
        }
        await settings.SetSettingAsync(MissionViewOptions.KeyCalendarHiddenEventTypes, MissionViewOptions.EventTypesToString(Opts.CalendarHiddenEventTypes));
        Changed?.Invoke();
    }

    public void PreviewCalendarMinChance(ChangeEventArgs e) {
        Opts.CalendarMinChance = MissionViewOptions.ParseChance(e.Value?.ToString(), Opts.CalendarMinChance);
        Changed?.Invoke();
    }

    public async Task SetCalendarMinChanceAsync(ChangeEventArgs e) {
        Opts.CalendarMinChance = MissionViewOptions.ParseChance(e.Value?.ToString(), Opts.CalendarMinChance);
        await settings.SetSettingAsync(MissionViewOptions.KeyCalendarMinChance, Opts.CalendarMinChance.ToString(System.Globalization.CultureInfo.InvariantCulture));
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
        if (Opts.SortByDropCount && Opts.MultiViewMode == MultiViewMode.Row) await SetMultiViewAsync(MultiViewMode.Off);

        Regroup();
        Changed?.Invoke();
    }

    public async Task SetSortMethodAsync(MissionSortMethod method) {
        Opts.SortMethod = method;
        await settings.SetSettingAsync(MissionViewOptions.KeySortMethod, MissionViewOptions.SortMethodToString(method));
        if (Actions is { } actions) await actions.ResortAsync(method);

        Changed?.Invoke();
    }

    public void Dispose() {
        active.Changed -= OnAccountChanged;
        hub.AccountInvalidated -= OnAccountInvalidated;
        timeZones.Changed -= OnAccountChanged;
    }

    private void OnAccountChanged() =>
        _ = _dispatch?.Invoke(LoadAsync);

    private void OnAccountInvalidated(string accountId) {
        if (accountId == active.ActiveAccountId) _ = _dispatch?.Invoke(LoadAsync);
    }

    private async Task<IReadOnlyList<MissionDrop>?> DropsFetcher(string accountId, string missionId) {
        _allDrops ??= await hub.GetDropsAsync(accountId) ?? [];
        return _allDrops.TryGetValue(missionId, out var drops) ? drops : [];
    }

    private async Task EnsureDropSummariesAsync() {
        if (!Enum.GetValues<CardSlot>().Any(slot => CardKinds.NeedsDrops(CardTemplates.For(slot).Doc))) return;
        if (DropSummaries.Count > 0) return;
        if (AllMissions is not { } missions || active.ActiveAccountId is not { } id) return;

        _allDrops ??= await hub.GetDropsAsync(id) ?? [];
        var summaries = new Dictionary<string, MissionDropSummary>();
        foreach (var mission in missions) {
            if (_allDrops.TryGetValue(mission.MissiondId, out var drops) && drops.Count > 0) {
                summaries[mission.MissiondId] = MissionDropSummary.From(drops, 12);
            }
        }

        DropSummaries = summaries;
        DesignerSamples = BuildDesignerSamples();
    }

    private List<MissionCardData> BuildDesignerSamples() {
        if (TabFiltered is not { Count: > 0 } missions) return [];

        IEnumerable<DatabaseMission?> picks = [
            missions[0],
            missions.FirstOrDefault(m => m.IsDubCap),
            missions.FirstOrDefault(m => m.IsBuggedCap),
            .. missions.DistinctBy(m => m.DurationType)
        ];

        return [.. picks
            .OfType<DatabaseMission>()
            .DistinctBy(m => m.MissiondId)
            .Take(8)
            .Select(DataFor)];
    }

    private void Regroup() {
        TabFiltered = MissionViewOptions.TabFilteredMissions(Filtered, Opts.MissionTypeTab);
        TabFilteredAll = MissionViewOptions.TabFilteredMissions(AllMissions, Opts.MissionTypeTab);
        Grouping = MissionGrouper.Group(TabFiltered, ts => MissionFilterMatcher.LedgerDate(ts, timeZones.TimeZone), true);
        DesignerSamples = BuildDesignerSamples();

        if (Opts.SortByDropCount && DropCounts.Count > 0 && TabFiltered is { Count: > 0 }) {
            var sorted = new List<DatabaseMission>(TabFiltered);
            sorted.Sort((a, b) => {
                DropCounts.TryGetValue(a.MissiondId, out var ca);
                DropCounts.TryGetValue(b.MissiondId, out var cb);
                var cmp = cb.CompareTo(ca);
                return cmp != 0 ? cmp : b.LaunchDT.CompareTo(a.LaunchDT);
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
        foreach (var m in Filtered) valid.Add(m.MissiondId);

        MultiSelectedIds.RemoveWhere(id => !valid.Contains(id));
    }
}
