using EggLedger.Web.Data;
using EggLedger.Web.State;
using Microsoft.Extensions.Logging;

namespace EggLedger.Web.Services;

public sealed class FetchOrchestrator(
    FetchService fetch,
    AppStateService appState,
    IndexedDbSettings settings,
    ILogger<FetchOrchestrator> logger,
    TimeProvider? time = null) : IDisposable {
    private const string InProgressKeyPrefix = "fetch_in_progress:";
    private const int LogCap = 2000;
    private const int RowLogCap = 50;
    private const int MaxRowsBeforeHidingDone = 5;
    private static readonly TimeSpan AutoHideGracePeriod = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan ReapTick = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan RowLinger = TimeSpan.FromSeconds(5);

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Lock _gate = new();
    private readonly List<FetchLogEntry> _log = [];
    private readonly List<MissionProcess> _rows = [];
    private CancellationTokenSource? _cts;
    private Timer? _autoHideTimer;
    private ITimer? _flushTimer;
    private ITimer? _reapTimer;
    private bool _flushPending;
    private DateTimeOffset? _missionsStartedAt;

    public static async Task<List<string>> GetIncompleteAccountsAsync(IndexedDbSettings store) {
        var all = await store.GetAllSettingsAsync().ConfigureAwait(false);
        return [.. all.Keys.Where(k => k.StartsWith(InProgressKeyPrefix, StringComparison.Ordinal))
            .Select(k => k[InProgressKeyPrefix.Length..])];
    }

    public FetchProgress? Progress { get; private set; }
    public AppState? TerminalState { get; private set; }
    public string? FetchingAccountId { get; private set; }

    public bool HasFetchContent { get; private set; }
    public bool LogExpanded { get; private set; }

    public IReadOnlyList<FetchLogEntry> Log { get; private set; } = [];
    public IReadOnlyList<MissionProcess> Processes { get; private set; } = [];
    public StageStatus SaveStage { get; private set; }
    public StageStatus MissionsStage { get; private set; }
    public StageStatus ExportStage { get; private set; }
    public string? CurrentMissionLabel { get; private set; }
    public IReadOnlyList<string> ExportedFiles { get; private set; } = [];

    public TimeSpan? Eta =>
        MissionsStage == StageStatus.Active && _missionsStartedAt is { } start && Progress is { Finished: >= 3 } p
            ? (_time.GetUtcNow() - start) / p.Finished * Math.Max(p.Total - p.Finished, 0)
            : null;

    public bool IsIdle => TerminalState is not null
                           || Progress is null
                           || Progress.State is AppState.AwaitingInput
                               or AppState.Success or AppState.Failed or AppState.Interrupted;

    public int Percent =>
        TerminalState == AppState.Success ? 100
        : Progress is { Total: > 0 } p ? (int)Math.Round((double)p.Finished / p.Total * 100) : 0;

    public event Action? Changed;
    public event Action<string>? FetchSucceeded;

    public void ToggleLog() {
        LogExpanded = !LogExpanded;
        if (LogExpanded) {
            _autoHideTimer?.Dispose();
            _autoHideTimer = null;
        } else {
            ScheduleAutoHide();
        }
        Changed?.Invoke();
    }

    private void ScheduleAutoHide() {
        if (TerminalState is null) {
            return;
        }

        _autoHideTimer?.Dispose();
        _autoHideTimer = new Timer(_ => ClearFetchContent(), null, AutoHideGracePeriod, Timeout.InfiniteTimeSpan);
    }

    private void ClearFetchContent() {
        HasFetchContent = false;
        LogExpanded = false;
        TerminalState = null;
        Progress = null;
        ResetRunState();
        Flush();
    }

    private void ResetRunState() {
        lock (_gate) {
            _log.Clear();
            _rows.Clear();
            SaveStage = StageStatus.Pending;
            MissionsStage = StageStatus.Pending;
            ExportStage = StageStatus.Pending;
            CurrentMissionLabel = null;
            ExportedFiles = [];
            _missionsStartedAt = null;
        }
    }

    public async Task StartFetchAsync(string accountId) {
        var previousTimer = _autoHideTimer;
        _autoHideTimer = null;
        TerminalState = null;
        HasFetchContent = false;
        LogExpanded = false;
        Progress = null;
        FetchingAccountId = accountId;
        _cts?.Cancel();
        _cts?.Dispose();
        var cts = _cts = new CancellationTokenSource();
        var token = cts.Token;
        ResetRunState();
        Flush();

        if (previousTimer is not null) {
            await previousTimer.DisposeAsync().ConfigureAwait(false);
        }

        await settings.SetSettingAsync(InProgressKeyPrefix + accountId, "1").ConfigureAwait(false);

        var progress = new Progress<FetchProgress>(p => {
            if (_cts != cts) {
                return;
            }
            Apply(p);
        });

        AppState result;
        try {
            result = await fetch.FetchPlayerDataAsync(accountId, progress, token);
        } catch (OperationCanceledException) when (token.IsCancellationRequested) {
            result = AppState.Failed;
        } catch (Exception ex) {
            logger.LogError(ex, "Fetch failed for account {AccountId}", accountId);
            result = AppState.Failed;
        }

        await settings.RemoveSettingAsync(InProgressKeyPrefix + accountId).ConfigureAwait(false);

        if (_cts != cts) {
            return;
        }

        lock (_gate) {
            ApplyStages(result, ExportedFiles);
        }
        TerminalState = result;
        HasFetchContent = true;
        appState.PipelineState = TerminalState;
        if (!LogExpanded) {
            ScheduleAutoHide();
        }
        Flush();

        if (result == AppState.Success) {
            FetchSucceeded?.Invoke(accountId);
        }
    }

    internal void Apply(FetchProgress p) {
        bool terminal = p.State is AppState.Success or AppState.Failed or AppState.Interrupted;
        lock (_gate) {
            if (TerminalState is null) {
                Progress = p.Segment is not null && Progress is not null ? p with {
                    Total = Progress.Total,
                    Finished = Progress.Finished,
                    Failed = Progress.Failed,
                    Retried = Progress.Retried,
                } : p;
            }

            var now = _time.GetUtcNow();
            if (p.LogText is { } text) {
                ApplyLog(p, text, now);
            }
            if (p is { MissionId: { } id, Segment: { } segment, SegmentStatus: { } status }) {
                ApplySegment(id, p.MissionLabel, segment, status, now);
            }
            if (p.Segment is null && p.LogText is null && (TerminalState is null || p.State == AppState.Success)) {
                ApplyStages(p.State, p.ExportedFiles);
            }
            if (Progress is { Total: > 0 } || _log.Count > 0) {
                HasFetchContent = true;
            }
        }

        if (TerminalState is null) {
            appState.PipelineState = p.State;
        }
        if (terminal) {
            Flush();
        } else {
            MarkDirty();
        }
    }

    private void ApplyLog(FetchProgress p, string text, DateTimeOffset now) {
        var entry = new FetchLogEntry(now, text, p.LogIsError);
        if (!p.LogRowOnly) {
            _log.Add(entry);
            if (_log.Count > LogCap) {
                _log.RemoveRange(0, _log.Count - LogCap);
            }
        }
        if (p.MissionId is not { } id) {
            return;
        }
        var row = Row(id, p.MissionLabel, now);
        row.Logs.Add(entry);
        if (row.Logs.Count > RowLogCap) {
            row.Logs.RemoveRange(0, row.Logs.Count - RowLogCap);
        }
        if (p.LogIsError && !p.LogRowOnly) {
            row.Status = ProcessStatus.Failed;
            row.EndedAt ??= now;
        }
    }

    private void ApplySegment(string id, string? label, string segment, SegmentStatus status, DateTimeOffset now) {
        var row = Row(id, label, now);
        int index = Array.FindIndex(row.Segments, s => s.Name == segment);
        if (index >= 0) {
            row.Segments[index] = row.Segments[index] with { Status = status };
        }
        if (status == SegmentStatus.Active) {
            CurrentMissionLabel = row.Label;
        }
        if (status == SegmentStatus.Done && segment is "Cache" or "Store") {
            row.Status = ProcessStatus.Done;
            row.EndedAt ??= now;
        }
    }

    private MissionProcess Row(string id, string? label, DateTimeOffset now) {
        var row = _rows.Find(r => r.Id == id);
        if (row is null) {
            row = new MissionProcess { Id = id, Label = label ?? id, StartedAt = now };
            _rows.Add(row);
        }
        return row;
    }

    private void ApplyStages(AppState state, IReadOnlyList<string> exported) {
        switch (state) {
            case AppState.FetchingSave:
                if (SaveStage == StageStatus.Pending) {
                    SaveStage = StageStatus.Active;
                }
                break;
            case AppState.FetchingMissions:
                SaveStage = StageStatus.Done;
                if (MissionsStage == StageStatus.Pending) {
                    MissionsStage = StageStatus.Active;
                    _missionsStartedAt = _time.GetUtcNow();
                }
                break;
            case AppState.ExportingData:
                SaveStage = StageStatus.Done;
                CloseMissionsStage();
                ExportStage = StageStatus.Active;
                break;
            case AppState.Success:
                SaveStage = StageStatus.Done;
                CloseMissionsStage();
                if (exported.Count > 0) {
                    ExportedFiles = exported;
                    ExportStage = StageStatus.Done;
                } else if (ExportStage is StageStatus.Pending or StageStatus.Active) {
                    ExportStage = StageStatus.Skipped;
                }
                CurrentMissionLabel = null;
                break;
            case AppState.Failed or AppState.Interrupted:
                SaveStage = FailIfActive(SaveStage);
                MissionsStage = FailIfActive(MissionsStage);
                ExportStage = FailIfActive(ExportStage);
                CurrentMissionLabel = null;
                break;
        }
    }

    private void CloseMissionsStage() {
        MissionsStage = MissionsStage switch {
            StageStatus.Pending => StageStatus.Skipped,
            StageStatus.Active => StageStatus.Done,
            var s => s,
        };
    }

    private static StageStatus FailIfActive(StageStatus s) =>
        s == StageStatus.Active ? StageStatus.Failed : s;

    private void MarkDirty() {
        lock (_gate) {
            if (_flushPending) {
                return;
            }
            _flushPending = true;
            _flushTimer ??= _time.CreateTimer(_ => Flush(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _flushTimer.Change(FlushInterval, Timeout.InfiniteTimeSpan);
        }
    }

    internal void Flush() {
        lock (_gate) {
            _flushPending = false;
            _flushTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

            var now = _time.GetUtcNow();
            _rows.RemoveAll(r => r.EndedAt is { } ended && now - ended >= RowLinger);
            var visible = _rows.Count > MaxRowsBeforeHidingDone
                ? _rows.Where(r => r.Status != ProcessStatus.Done)
                : _rows;
            Processes = [.. visible.Select(Clone)];
            Log = [.. _log];

            if (_rows.Exists(r => r.EndedAt is not null)) {
                _reapTimer ??= _time.CreateTimer(_ => Flush(), null, ReapTick, ReapTick);
            } else {
                _reapTimer?.Dispose();
                _reapTimer = null;
            }
        }
        Changed?.Invoke();
    }

    private static MissionProcess Clone(MissionProcess row) {
        var copy = new MissionProcess {
            Id = row.Id,
            Label = row.Label,
            StartedAt = row.StartedAt,
            Status = row.Status,
            EndedAt = row.EndedAt,
        };
        row.Segments.CopyTo(copy.Segments, 0);
        copy.Logs.AddRange(row.Logs);
        return copy;
    }

    public void StopFetch() =>
        _cts?.Cancel();

    public void Dispose() {
        _cts?.Cancel();
        _cts?.Dispose();
        _autoHideTimer?.Dispose();
        _flushTimer?.Dispose();
        _reapTimer?.Dispose();
    }
}
