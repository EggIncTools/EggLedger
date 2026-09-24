using System.Globalization;
using EggLedger.Domain.Api;
using EggLedger.Domain.Ei;
using EggLedger.Domain.MissionPacking;
using EggLedger.Domain.MissionQuery;
using EggLedger.Domain.Util;
using EggLedger.Web.Data;
using EggLedger.Web.Settings;
using Ei;
using Microsoft.Extensions.Logging;

namespace EggLedger.Web.Services;

public sealed class FetchService(ApiClient api, IndexedDbMissionStore store, IndexedDbSettings settings, IndexedDbAccountStore accounts, IApiPayloadDecoder decoder, ILogger<FetchService> logger, MissionPacker? packer = null, IAutoExporter? autoExporter = null, TimeProvider? time = null) {
    private const int DefaultWorkerCount = 1;
    private const int MaxWorkerCount = 10;
    private static readonly TimeSpan BackupMinGap = TimeSpan.FromHours(12);
    private readonly MissionPacker _packer = packer ?? new MissionPacker(EiafxMissionConfigSource.Instance);
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public async Task<AppState> FetchPlayerDataAsync(
        string playerId,
        IProgress<FetchProgress>? progress,
        CancellationToken cancellationToken) {
        int total = 0;
        int finished = 0;
        int failed = 0;
        int retried = 0;
        var current = AppState.FetchingSave;
        var labelById = new Dictionary<string, string>(StringComparer.Ordinal);

        FetchProgress Snapshot(AppState state) => new() {
            State = state,
            Total = Volatile.Read(ref total),
            Finished = Volatile.Read(ref finished),
            Failed = Volatile.Read(ref failed),
            Retried = Volatile.Read(ref retried),
        };

        void Report(AppState state) {
            current = state;
            progress?.Report(Snapshot(state));
        }

        void Log(string text, bool isError, string? missionId = null, bool rowOnly = false) {
            progress?.Report(Snapshot(current) with {
                LogText = text,
                LogIsError = isError,
                LogRowOnly = rowOnly,
                MissionId = missionId,
                MissionLabel = missionId is not null && labelById.TryGetValue(missionId, out var label) ? label : null,
            });
        }

        void Info(string text) => Log(text, false);

        void Error(string text) => Log(text, true);

        AppState Interrupt() {
            Error("interrupted");
            Report(AppState.Interrupted);
            return AppState.Interrupted;
        }

        Report(AppState.FetchingSave);
        EggIncFirstContactResponse fc;
        AccountInfo account;
        try {
            (fc, account) = await FetchFirstContactAsync(playerId, cancellationToken).ConfigureAwait(false);
        } catch (OperationCanceledException) {
            return Interrupt();
        } catch (Exception ex) {
            Error(ex.Message);
            throw;
        }

        EmitSaveMessages(playerId, fc.Backup!, account, Info, Error);

        await StashInFlightMissionsAsync(playerId, fc).ConfigureAwait(false);

        var completed = fc.GetCompletedMissions();
        var existing = await store.GetCompleteMissionIdsAsync(playerId).ConfigureAwait(false) ?? [];
        var seen = new HashSet<string>(existing, StringComparer.Ordinal);

        var toFetch = new List<(string Id, double Start)>();
        foreach (var mission in completed) {
            if (seen.Add(mission.Identifier)) {
                toFetch.Add((mission.Identifier, mission.StartTimeDerived));
                labelById[mission.Identifier] = MissionLabel(mission);
            }
        }
        Volatile.Write(ref total, toFetch.Count);
        Info(string.Create(CultureInfo.InvariantCulture,
            $"found &148c32<{completed.Count} completed> missions, &148c32<{fc.GetInProgressMissions().Count} in-progress> missions, &148c32<{toFetch.Count} to fetch>"));

        if (cancellationToken.IsCancellationRequested) {
            return Interrupt();
        }

        var saved = await settings.GetAllSettingsAsync().ConfigureAwait(false);
        var failures = new List<FailedMission>();

        if (total > 0) {
            Report(AppState.FetchingMissions);

            int workerCount = ReadWorkerCount(saved);
            bool interrupted = await RunWorkersAsync(
                playerId, toFetch, labelById, workerCount, progress,
                () => {
                    Interlocked.Increment(ref finished);
                    Report(AppState.FetchingMissions);
                },
                (fm, cancelled) => {
                    Interlocked.Increment(ref failed);
                    lock (failures) {
                        failures.Add(fm);
                    }
                    if (!cancelled) {
                        Log(fm.Reason, true, fm.MissionId);
                    }
                },
                (missionId, text) => {
                    Interlocked.Increment(ref retried);
                    Log(text, true, missionId, rowOnly: true);
                },
                cancellationToken).ConfigureAwait(false);

            if (interrupted) {
                return Interrupt();
            }

            int failedCount = Volatile.Read(ref failed);
            if (failedCount > 0) {
                Error(string.Create(CultureInfo.InvariantCulture, $"{failedCount} of {total} missions failed to fetch"));
                Info("(performing another &7a7a7a<fetch> will fetch the failed missions most of the time)");
                current = AppState.Failed;
                progress?.Report(Snapshot(AppState.Failed) with { FailedMissions = [.. failures] });
                return AppState.Failed;
            }

            Info(string.Create(CultureInfo.InvariantCulture, $"successfully fetched &148c32<{total} missions>"));
        }

        Report(AppState.ExportingData);
        IReadOnlyList<string> exported = [];
        if (autoExporter is not null) {
            try {
                exported = await autoExporter.RunAfterFetchAsync(playerId, cancellationToken).ConfigureAwait(false);
            } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                return Interrupt();
            } catch (Exception ex) {
                logger.LogError(ex, "auto-export failed for player {PlayerId}", playerId);
                Error(ex.Message);
                Report(AppState.Failed);
                return AppState.Failed;
            }
        }

        Info("done.");
        current = AppState.Success;
        progress?.Report(Snapshot(AppState.Success) with { ExportedFiles = exported });
        return AppState.Success;
    }

    private void EmitSaveMessages(string playerId, Backup backup, AccountInfo account, Action<string> info, Action<string> error) {
        var (roleColor, roleName, _, _, _) = Role.RoleFromEB(backup.GetEarningsBonus());
        var fetched = $"successfully fetched backup for &7a7a7a<{playerId}>";
        if (!string.IsNullOrEmpty(account.Nickname)) {
            fetched += $" (&{roleColor}<{account.Nickname}>)";
        }
        info(fetched);

        var eggs = account.TeCount > 0
            ? string.Create(CultureInfo.InvariantCulture, $"  [img:truth_egg.png] &c831ff<{account.TeCount} TE>")
            : "";
        eggs += string.Create(CultureInfo.InvariantCulture,
            $"  [img:soul_egg.png] &a855f7<{account.SeString} SE>  [img:prophecy_egg.png] &eab308<{account.PeCount} PE>");
        info(eggs);

        info($"updated local database EB to &{roleColor}<{account.EBString}>, role to &{roleColor}<{roleName}>");

        if (account.LastBackupTime != 0) {
            var now = _time.GetUtcNow();
            var at = TimeFmt.UnixToTime(account.LastBackupTime);
            info($"backup is from &7a7a7a<{TimeFmt.HumanizeTime(at > now ? now : at, now)}>");
        } else {
            error("backup is from unknown time");
        }
    }

    internal static string MissionLabel(MissionInfo mission) =>
        $"{mission.Ship.Name()}, {mission.duration_type.Display()}, {TimeFmt.UnixToTime(mission.StartTimeDerived).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";

    private async Task StashInFlightMissionsAsync(string playerId, EggIncFirstContactResponse fc) {
        List<DatabaseMission> missions = [.. fc.GetInProgressMissions().Select(_packer.CompileInFlightMission)];

        _ = await store.ReplaceInFlightMissionsAsync(playerId, missions).ConfigureAwait(false);
    }

    private async Task<(EggIncFirstContactResponse Fc, AccountInfo Account)> FetchFirstContactAsync(string playerId, CancellationToken cancellationToken) {
        byte[] payload = await api.RequestFirstContactRawPayloadAsync(playerId, cancellationToken).ConfigureAwait(false);
        var fc = await decoder.DecodeFirstContactAsync(payload, cancellationToken).ConfigureAwait(false);
        var invalid = fc.Validate();
        if (invalid is not null) {
            throw new InvalidOperationException(
                $"please double check your ID: error fetching backup for player {playerId}: {invalid.Message}", invalid);
        }

        double lastBackupTime = fc.Backup?.settings?.LastBackupTime ?? 0;
        if (lastBackupTime != 0) {

            try {
                await store.InsertBackupAsync(playerId, lastBackupTime, payload, BackupMinGap).ConfigureAwait(false);
            } catch (Exception ex) {
                logger.LogDebug(ex, "backup insert failed for player {PlayerId}", playerId);
            }
        }

        var account = AccountFactory.FromBackup(playerId, fc.Backup!);
        await accounts.AddKnownAccountAsync(account).ConfigureAwait(false);

        return (fc, account);
    }

    private async Task<bool> RunWorkersAsync(
        string playerId,
        List<(string Id, double Start)> missions,
        Dictionary<string, string> labelById,
        int workerCount,
        IProgress<FetchProgress>? progress,
        Action onFinished,
        Action<FailedMission, bool> onError,
        Action<string, string> onRetry,
        CancellationToken cancellationToken) {
        using var sem = new SemaphoreSlim(workerCount, workerCount);
        var tasks = new List<Task>(missions.Count);

        foreach (var (id, start) in missions) {
            if (cancellationToken.IsCancellationRequested) {
                break;
            }
            await sem.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested) {
                sem.Release();
                break;
            }
            var label = labelById[id];
            tasks.Add(Task.Run(async () => {
                try {
                    await FetchMissionWithRetriesAsync(playerId, id, label, start, progress, onRetry, cancellationToken).ConfigureAwait(false);
                } catch (OperationCanceledException) {

                    onError(new FailedMission(id, start, "cancelled"), true);
                } catch (Exception ex) {
                    onError(new FailedMission(id, start, ex.Message), false);
                } finally {
                    onFinished();
                    sem.Release();
                }
            }, CancellationToken.None));
        }

        try {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        } catch (OperationCanceledException ex) {
            logger.LogDebug(ex, "mission workers cancelled for player {PlayerId}", playerId);
        }

        return cancellationToken.IsCancellationRequested;
    }

    private const int MaxRetryAttempts = 5;
    private static readonly TimeSpan RetryBaseDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(4);

    private async Task FetchMissionWithRetriesAsync(
        string playerId,
        string missionId,
        string label,
        double startTimestamp,
        IProgress<FetchProgress>? progress,
        Action<string, string> onRetry,
        CancellationToken cancellationToken) {
        for (int attempt = 0; attempt <= MaxRetryAttempts; attempt++) {
            try {
                await FetchOneMissionAsync(playerId, missionId, label, startTimestamp, progress, cancellationToken).ConfigureAwait(false);
                return;
            } catch (OperationCanceledException) {
                throw;
            } catch (Exception ex) when (attempt < MaxRetryAttempts && !cancellationToken.IsCancellationRequested) {
                logger.LogDebug(ex, "mission fetch attempt {Attempt} failed for {MissionId}, retrying", attempt, missionId);
                var delay = RetryDelay(attempt + 1);
                onRetry(missionId, string.Create(CultureInfo.InvariantCulture,
                    $"attempt {attempt + 1} failed: {ex.Message}, retrying in {delay.TotalSeconds:0.#}s"));
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static TimeSpan RetryDelay(int retry) =>
        TimeSpan.FromMilliseconds(Math.Min(
            RetryBaseDelay.TotalMilliseconds * Math.Pow(2, retry - 1),
            MaxRetryDelay.TotalMilliseconds));

    private async Task FetchOneMissionAsync(
        string playerId,
        string missionId,
        string label,
        double startTimestamp,
        IProgress<FetchProgress>? progress,
        CancellationToken cancellationToken) {
        void Track(string segment, SegmentStatus status) {
            progress?.Report(new FetchProgress {
                State = AppState.FetchingMissions,
                MissionId = missionId,
                MissionLabel = label,
                Segment = segment,
                SegmentStatus = status,
            });
        }

        Track("Cache", SegmentStatus.Active);
        var cached = await store.GetCompleteMissionAsync(playerId, missionId).ConfigureAwait(false);
        if (cached is not null) {
            Track("Cache", SegmentStatus.Done);
            return;
        }
        Track("Cache", SegmentStatus.Skipped);

        Track("Fetch", SegmentStatus.Active);
        byte[] payload;
        try {
            payload = await api.RequestCompleteMissionRawPayloadAsync(playerId, missionId, cancellationToken).ConfigureAwait(false);
        } catch {
            Track("Fetch", SegmentStatus.Failed);
            throw;
        }
        Track("Fetch", SegmentStatus.Done);

        Track("Decode", SegmentStatus.Active);
        CompleteMissionResponse resp;
        try {
            resp = await decoder.DecodeCompleteMissionAsync(payload, cancellationToken).ConfigureAwait(false);
        } catch {
            Track("Decode", SegmentStatus.Failed);
            throw;
        }
        if (!resp.Success) {
            Track("Decode", SegmentStatus.Failed);
            throw new InvalidOperationException(
                $"error fetching mission {missionId} for player {playerId}: success is false");
        }
        if (resp.Artifacts.Count == 0) {
            Track("Decode", SegmentStatus.Failed);
            throw new InvalidOperationException(
                $"error fetching mission {missionId} for player {playerId}: no artifact found in server response");
        }
        Track("Decode", SegmentStatus.Done);

        Track("Store", SegmentStatus.Active);
        int missionType = resp.Info is not null ? (int)resp.Info.Type : -1;
        _packer.TryComputeMissionFilterCols(startTimestamp, resp, out var cols);
        try {
            await store.InsertCompleteMissionAsync(
                playerId, missionId, startTimestamp, payload, missionType, cols, resp).ConfigureAwait(false);
        } catch {
            Track("Store", SegmentStatus.Failed);
            throw;
        }
        Track("Store", SegmentStatus.Done);
    }

    private static int ReadWorkerCount(Dictionary<string, string> saved) {
        int n = DefaultWorkerCount;
        if (saved.TryGetValue(SettingsModel.KeyWorkerCount, out var raw)
            && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)) {
            n = parsed;
        }
        return Math.Clamp(n, 1, MaxWorkerCount);
    }
}
