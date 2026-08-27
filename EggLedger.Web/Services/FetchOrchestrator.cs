using EggLedger.Web.Data;
using EggLedger.Web.State;
using Microsoft.Extensions.Logging;

namespace EggLedger.Web.Services;

public sealed class FetchOrchestrator : IDisposable {
    private const string InProgressKeyPrefix = "fetch_in_progress:";
    private static readonly TimeSpan AutoHideGracePeriod = TimeSpan.FromSeconds(12);

    private readonly FetchService _fetch;
    private readonly AppStateService _appState;
    private readonly IndexedDbSettings _settings;
    private readonly ILogger<FetchOrchestrator> _logger;
    private readonly IAutoExporter? _autoExporter;
    private CancellationTokenSource? _cts;
    private Timer? _autoHideTimer;

    public FetchOrchestrator(
        FetchService fetch,
        AppStateService appState,
        IndexedDbSettings settings,
        ILogger<FetchOrchestrator> logger,
        IAutoExporter? autoExporter = null) {
        _fetch = fetch;
        _appState = appState;
        _settings = settings;
        _logger = logger;
        _autoExporter = autoExporter;
    }

    public static async Task<List<string>> GetIncompleteAccountsAsync(IndexedDbSettings settings) {
        var all = await settings.GetAllSettingsAsync().ConfigureAwait(false);
        return [.. all.Keys.Where(k => k.StartsWith(InProgressKeyPrefix, StringComparison.Ordinal))
            .Select(k => k[InProgressKeyPrefix.Length..])];
    }

    public FetchProgress? Progress { get; private set; }
    public AppState? TerminalState { get; private set; }
    public string? FetchingAccountId { get; private set; }

    public bool HasFetchContent { get; private set; }
    public bool LogExpanded { get; private set; }

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
        Changed?.Invoke();
    }

    public async Task StartFetchAsync(string accountId) {
        _autoHideTimer?.Dispose();
        _autoHideTimer = null;
        TerminalState = null;
        HasFetchContent = false;
        LogExpanded = false;
        FetchingAccountId = accountId;
        _cts?.Cancel();
        _cts?.Dispose();
        var cts = _cts = new CancellationTokenSource();
        var token = cts.Token;

        await _settings.SetSettingAsync(InProgressKeyPrefix + accountId, "1").ConfigureAwait(false);

        var progress = new Progress<FetchProgress>(p => {
            if (_cts != cts) {
                return;
            }


            var segmentOnly = p.Segment is not null;
            if (segmentOnly && Progress is not null) {
                Progress = p with {
                    Total = Progress.Total,
                    Finished = Progress.Finished,
                    Failed = Progress.Failed,
                    Retried = Progress.Retried
                };
            } else {
                Progress = p;
            }

            if (Progress is { Total: > 0 }) {
                HasFetchContent = true;
            }

            _appState.PipelineState = p.State;
            Changed?.Invoke();
        });

        AppState result;
        try {
            result = await _fetch.FetchPlayerDataAsync(accountId, progress, token);
        } catch (OperationCanceledException) when (token.IsCancellationRequested) {
            result = AppState.Failed;
        } catch (Exception ex) {
            _logger.LogError(ex, "Fetch failed for account {AccountId}", accountId);
            result = AppState.Failed;
        }




        await _settings.RemoveSettingAsync(InProgressKeyPrefix + accountId).ConfigureAwait(false);



        if (_cts != cts) {
            return;
        }

        TerminalState = result;
        HasFetchContent = true;
        _appState.PipelineState = TerminalState;
        if (!LogExpanded) {
            ScheduleAutoHide();
        }
        Changed?.Invoke();

        if (result == AppState.Success) {
            FetchSucceeded?.Invoke(accountId);
            await TryAutoExportAsync(accountId).ConfigureAwait(false);
        }
    }

    private async Task TryAutoExportAsync(string accountId) {
        if (_autoExporter is null) {
            return;
        }

        try {
            await _autoExporter.RunAfterFetchAsync(accountId).ConfigureAwait(false);
        } catch (Exception ex) {
            _logger.LogError(ex, "Auto-export after fetch failed for account {AccountId}", accountId);
        }
    }

    public void StopFetch() {
        _cts?.Cancel();
    }

    public void Dispose() {
        _cts?.Cancel();
        _cts?.Dispose();
        _autoHideTimer?.Dispose();
    }
}
