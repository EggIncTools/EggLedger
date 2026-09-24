using System.IO.Compression;
using System.Text.Json;
using EggLedger.Domain.Api;
using EggLedger.Domain.MissionPacking;
using EggLedger.Domain.MissionQuery;
using Ei;
using Microsoft.Extensions.Logging;

namespace EggLedger.Web.Data;

public sealed class IndexedDbMissionStore(IIndexedDb db, IApiPayloadDecoder decoder, MissionPacker? packer = null, IndexedDbAccountStore? accounts = null, ILogger<IndexedDbMissionStore>? logger = null, TimeProvider? time = null) : IMissionStore {
    private readonly MissionPacker _packer = packer ?? new MissionPacker(EiafxMissionConfigSource.Instance);
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public async Task<IReadOnlyList<string>?> GetCompleteMissionIdsAsync(string playerId) {
        var rows = await PlayerMetaRowsAsync(playerId);
        return [.. rows.OrderBy(r => r.StartTimestamp).Select(r => r.MissionId)];
    }

    public async Task<IReadOnlyList<KnownAccount>> GetKnownAccountsAsync() {
        if (accounts is null) {
            return [];
        }
        var known = await accounts.GetKnownAccountsAsync().ConfigureAwait(false);
        return [.. known.Select(a => a.ToKnownAccount())];
    }

    public async Task<PlayerMissionStats?> GetPlayerMissionStatsAsync(string playerId) {
        var rows = await PlayerMetaRowsAsync(playerId);
        double max = rows.Count == 0 ? 0 : rows.Max(r => r.ReturnTimestamp);
        return new PlayerMissionStats(rows.Count, max);
    }

    public async Task<bool> StreamPlayerCompleteMissionsAsync(string playerId, Action<CompleteMissionResponse> onMission) {
        var rows = await PlayerRowsAsync(playerId);
        foreach (var row in rows.OrderBy(r => r.StartTimestamp)) {
            CompleteMissionResponse cm;

            try {
                cm = await DecodeAsync(row).ConfigureAwait(false);
            } catch (Exception ex) {
                logger?.LogDebug(ex, "mission decode failed while streaming {MissionId}", row.MissionId);
                return false;
            }
            onMission(cm);
        }
        return true;
    }

    public async Task<CompleteMissionResponse?> GetCompleteMissionAsync(string playerId, string missionId) {

        var key = DecodeKey(playerId, missionId);
        if (DecodeCacheGet(key) is { } hit) {
            return hit;
        }
        var row = await db.GetAsync<MissionRow>(IndexedDbStores.Mission, new object[] { playerId, missionId }).ConfigureAwait(false);
        if (row is null) {
            return null;
        }
        try {
            var decoded = await DecodeAsync(row).ConfigureAwait(false);
            DecodeCachePut(key, decoded);
            return decoded;
        } catch (Exception ex) {
            logger?.LogDebug(ex, "mission decode failed for {MissionId}", missionId);
            return null;
        }
    }

    public async Task<int?> CountPendingFilterColsAsync(string eid) {
        var rows = await PlayerMetaRowsAsync(eid);
        return rows.Count(r => r.Ship == -1);
    }

    public async Task<IReadOnlyList<IMissionRow>?> GetPlayerMissionMetaAsync(string eid) {
        var rows = await PlayerMetaRowsAsync(eid);
        List<IMissionRow> result = [.. rows
            .Where(r => r.Ship != -1)
            .OrderBy(r => r.StartTimestamp)
            .Select(row => _packer.MissionMetaToDBMission(ToMeta(row)))];
        return result;
    }

    public async Task<IReadOnlyList<CompleteMissionResponse>?> GetPlayerCompleteMissionsAsync(string eid) {
        var rows = (await PlayerRowsAsync(eid)).OrderBy(r => r.StartTimestamp).ToList();
        try {

            var result = new CompleteMissionResponse[rows.Count];
            const int batch = 16;
            for (int start = 0; start < rows.Count; start += batch) {
                int end = Math.Min(start + batch, rows.Count);
                var tasks = new Task[end - start];
                foreach (int idx in Enumerable.Range(start, end - start)) {
                    tasks[idx - start] = Assign(idx);
                }
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            return result;

            async Task Assign(int idx) {
                result[idx] = await DecodeAsync(rows[idx]).ConfigureAwait(false);
            }
        } catch (Exception ex) {
            logger?.LogDebug(ex, "complete mission decode failed for player {PlayerId}", eid);
            return null;
        }
    }

    private const int DecodeCacheCap = 256;
    private readonly Lock _decodeGate = new();
    private readonly LinkedList<(string Key, CompleteMissionResponse Value)> _decodeLru = new();
    private readonly Dictionary<string, LinkedListNode<(string Key, CompleteMissionResponse Value)>> _decodeIndex = [with(StringComparer.Ordinal)];

    private static string DecodeKey(string playerId, string missionId) => playerId + " " + missionId;

    private CompleteMissionResponse? DecodeCacheGet(string key) {
        lock (_decodeGate) {
            if (!_decodeIndex.TryGetValue(key, out var node)) {
                return null;
            }
            _decodeLru.Remove(node);
            _decodeLru.AddFirst(node);
            return node.Value.Value;
        }
    }

    private void DecodeCachePut(string key, CompleteMissionResponse value) {
        lock (_decodeGate) {
            if (_decodeIndex.TryGetValue(key, out var existing)) {
                _decodeLru.Remove(existing);
                _decodeIndex.Remove(key);
            }
            var node = _decodeLru.AddFirst((key, value));
            _decodeIndex[key] = node;
            while (_decodeIndex.Count > DecodeCacheCap) {
                var last = _decodeLru.Last!;
                _decodeLru.RemoveLast();
                _decodeIndex.Remove(last.Value.Key);
            }
        }
    }

    private void DecodeCacheEvict(string key) {
        lock (_decodeGate) {
            if (_decodeIndex.TryGetValue(key, out var node)) {
                _decodeLru.Remove(node);
                _decodeIndex.Remove(key);
            }
        }
    }

    private readonly Lock _backfillGate = new();
    private readonly Dictionary<string, Task> _backfillTasks = [with(StringComparer.Ordinal)];

    public void QueueFilterColBackfill(string eid) =>
        _ = GetOrStartFilterColBackfillAsync(eid);

    public Task EnsureFilterColsBackfilledAsync(string eid) =>
        GetOrStartFilterColBackfillAsync(eid);

    private Task GetOrStartFilterColBackfillAsync(string eid) {
        lock (_backfillGate) {
            if (_backfillTasks.TryGetValue(eid, out var existing))
                return existing;

            var task = BackfillFilterColsAsync(eid);
            _backfillTasks[eid] = task;
            return task;
        }
    }

    private async Task BackfillFilterColsAsync(string eid) {
        try {
            foreach (var row in await PlayerRowsAsync(eid).ConfigureAwait(false)) {
                if (row.Ship != -1)
                    continue;

                CompleteMissionResponse decoded;
                try {
                    decoded = await DecodeAsync(row).ConfigureAwait(false);
                } catch (Exception ex) {
                    logger?.LogDebug(ex, "filter column backfill decode failed for {MissionId}", row.MissionId);
                    continue;
                }

                if (_packer.TryComputeMissionFilterCols(row.StartTimestamp, decoded, out var cols)) {
                    await db.PutAsync(IndexedDbStores.Mission, WithCols(row, cols)).ConfigureAwait(false);
                    DecodeCacheEvict(DecodeKey(eid, row.MissionId));
                }
            }
        } finally {
            lock (_backfillGate) {
                _backfillTasks.Remove(eid);
            }
        }
    }

    private static MissionRow WithCols(MissionRow row, MissionFilterCols cols) => row with {
        Ship = cols.Ship,
        DurationType = cols.DurationType,
        Level = cols.Level,
        Capacity = cols.Capacity,
        NominalCapacity = cols.NominalCapacity,
        IsDubCap = cols.IsDubCap,
        IsBuggedCap = cols.IsBuggedCap,
        Target = cols.Target,
        ReturnTimestamp = cols.ReturnTimestamp,
    };

    private readonly HashSet<string> _dropsBackfilling = [];

    public void QueueArtifactDropsBackfill(string playerId) {
        if (_dropsBackfilling.Add(playerId))
            _ = BackfillArtifactDropsAsync(playerId);
    }

    private async Task BackfillArtifactDropsAsync(string playerId) {
        try {
            var missionIds = await GetCompleteMissionIdsAsync(playerId).ConfigureAwait(false);
            if (missionIds is null)
                return;
            var stored = await GetStoredPlayerDropsAsync(playerId).ConfigureAwait(false);
            if (stored is null)
                return;
            var haveRows = stored.Select(d => d.MissionId).ToHashSet();

            foreach (var missionId in missionIds) {
                if (haveRows.Contains(missionId))
                    continue;

                CompleteMissionResponse decoded;
                try {
                    decoded = await GetCompleteMissionAsync(playerId, missionId).ConfigureAwait(false) ?? throw new InvalidOperationException();
                } catch (Exception ex) {
                    logger?.LogDebug(ex, "artifact drops backfill decode failed for {MissionId}", missionId);
                    continue;
                }

                var drops = ArtifactDrops.Build(decoded);
                if (drops.Count == 0) {
                    await db.PutAsync(IndexedDbStores.ArtifactDrops, new ArtifactDropRow {
                        MissionId = missionId,
                        PlayerId = playerId,
                        DropIndex = -1,
                    }).ConfigureAwait(false);
                    continue;
                }

                var rows = drops.Select(d => (object)new ArtifactDropRow {
                    MissionId = missionId,
                    PlayerId = playerId,
                    DropIndex = d.DropIndex,
                    ArtifactId = d.ArtifactId,
                    SpecType = d.SpecType,
                    Level = d.Level,
                    Rarity = d.Rarity,
                    Quality = d.Quality,
                });
                await db.PutManyAsync(IndexedDbStores.ArtifactDrops, rows).ConfigureAwait(false);
            }
        } finally {
            _dropsBackfilling.Remove(playerId);
        }
    }

    public async Task InsertBackupAsync(string playerId, double timestamp, byte[] rawPayload, TimeSpan minimumGap) {
        if (minimumGap > TimeSpan.Zero) {
            var existing = await db.GetAsync<BackupRow>(IndexedDbStores.Backup, playerId);
            if (existing is not null) {
                double gapSeconds = timestamp - existing.RecordedAt;
                if (gapSeconds < minimumGap.TotalSeconds) {
                    return;
                }
            }
        }

        var row = new BackupRow {
            PlayerId = playerId,
            RecordedAt = timestamp,
            Payload = Gzip(rawPayload),
        };
        await db.PutAsync(IndexedDbStores.Backup, row);
    }

    public async Task InsertCompleteMissionAsync(
        string playerId,
        string missionId,
        double startTimestamp,
        byte[] rawPayload,
        int missionType,
        MissionFilterCols cols,
        CompleteMissionResponse decoded) {
        var row = new MissionRow {
            PlayerId = playerId,
            MissionId = missionId,
            StartTimestamp = startTimestamp,
            CompletePayload = Gzip(rawPayload),
            MissionType = missionType,
            Ship = cols.Ship,
            DurationType = cols.DurationType,
            Level = cols.Level,
            Capacity = cols.Capacity,
            NominalCapacity = cols.NominalCapacity,
            IsDubCap = cols.IsDubCap,
            IsBuggedCap = cols.IsBuggedCap,
            Target = cols.Target,
            ReturnTimestamp = cols.ReturnTimestamp,
        };
        await db.PutAsync(IndexedDbStores.Mission, row);
        DecodeCacheEvict(DecodeKey(playerId, missionId));

        var drops = ArtifactDrops.Build(decoded);
        if (drops.Count > 0) {
            var rows = drops.Select(d => (object)new ArtifactDropRow {
                MissionId = missionId,
                PlayerId = playerId,
                DropIndex = d.DropIndex,
                ArtifactId = d.ArtifactId,
                SpecType = d.SpecType,
                Level = d.Level,
                Rarity = d.Rarity,
                Quality = d.Quality,
            });
            await db.PutManyAsync(IndexedDbStores.ArtifactDrops, rows);
        }

        var fuel = MissionFuels.Build(decoded);
        if (fuel.Count > 0) {
            var fuelRows = fuel.Select(f => (object)new FuelRow {
                MissionId = missionId,
                PlayerId = playerId,
                FuelIndex = f.FuelIndex,
                EggId = f.EggId,
                Amount = f.Amount,
            });
            await db.PutManyAsync(IndexedDbStores.MissionFuel, fuelRows);
        }
    }

    public async Task<bool> ReplaceInFlightMissionsAsync(string playerId, IReadOnlyList<DatabaseMission> missions) {
        long capturedAt = _time.GetUtcNow().ToUnixTimeSeconds();
        var keep = new HashSet<string>(StringComparer.Ordinal);
        var rows = new List<object>(missions.Count);
        foreach (var mission in missions) {
            if (!keep.Add(mission.MissiondId)) {
                continue;
            }
            rows.Add(new InFlightMissionRow {
                PlayerId = playerId,
                MissionId = mission.MissiondId,
                CapturedAt = capturedAt,
                Payload = JsonSerializer.Serialize(mission, Rows.JsonOptions),
            });
        }

        try {
            if (rows.Count > 0) {
                await db.PutManyAsync(IndexedDbStores.InFlightMission, rows).ConfigureAwait(false);
            }

            var stored = await db
                .GetAllByIndexAsync<InFlightMissionRow>(IndexedDbStores.InFlightMission, IndexedDbStores.PlayerIdIndex, playerId)
                .ConfigureAwait(false);
            foreach (var stale in stored.Select(r => r.MissionId).Where(id => !keep.Contains(id))) {
                await db.DeleteAsync(
                    IndexedDbStores.InFlightMission, new object[] { playerId, stale }).ConfigureAwait(false);
            }
            return true;
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            logger?.LogWarning(ex, "failed to persist in-flight missions for player {PlayerId}", playerId);
            return false;
        }
    }

    public async Task<IReadOnlyList<DatabaseMission>> GetInFlightMissionsAsync(string playerId) {
        InFlightMissionRow[] rows;
        try {
            rows = await db
                .GetAllByIndexAsync<InFlightMissionRow>(IndexedDbStores.InFlightMission, IndexedDbStores.PlayerIdIndex, playerId)
                .ConfigureAwait(false);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            logger?.LogWarning(ex, "failed to read in-flight missions for player {PlayerId}", playerId);
            return [];
        }

        var result = new List<DatabaseMission>(rows.Length);
        foreach (var row in rows) {
            DatabaseMission? mission;
            try {
                mission = JsonSerializer.Deserialize<DatabaseMission>(row.Payload, Rows.JsonOptions);
            } catch (JsonException ex) {
                logger?.LogDebug(ex, "in-flight mission payload unreadable for {MissionId}", row.MissionId);
                continue;
            }
            if (mission is not null) {
                result.Add(mission);
            }
        }

        result.Sort((a, b) => a.LaunchDT.CompareTo(b.LaunchDT));
        return result;
    }

    private static byte[] Gzip(byte[] data) {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionMode.Compress, leaveOpen: true)) {
            gzip.Write(data, 0, data.Length);
        }
        return output.ToArray();
    }

    private async Task<List<MissionRow>> PlayerRowsAsync(string playerId) {
        var rows = await db.GetAllByIndexAsync<MissionRow>(IndexedDbStores.Mission, IndexedDbStores.PlayerIdIndex, playerId);
        return [.. rows];
    }


    private async Task<List<MissionMetaRow>> PlayerMetaRowsAsync(string playerId) {
        var rows = await db.GetAllByIndexProjectedAsync<MissionMetaRow>(IndexedDbStores.Mission, IndexedDbStores.PlayerIdIndex, playerId);
        return [.. rows];
    }

    public async Task<IReadOnlyList<StoredDrop>?> GetStoredPlayerDropsAsync(string playerId) {
        try {
            var rows = await db.GetAllByIndexAsync<ArtifactDropRow>(IndexedDbStores.ArtifactDrops, IndexedDbStores.PlayerIdIndex, playerId);
            return [.. rows.Select(r => new StoredDrop(r.MissionId, r.ArtifactId, r.Level, r.Rarity, r.DropIndex))];
        } catch (Exception ex) {
            logger?.LogDebug(ex, "stored drops read failed for player {PlayerId}", playerId);
            return null;
        }
    }

    public async Task DeleteAllForPlayerAsync(string playerId) {
        var missionRows = await PlayerMetaRowsAsync(playerId).ConfigureAwait(false);
        foreach (var row in missionRows) {
            await db.DeleteAsync(IndexedDbStores.Mission, new object[] { playerId, row.MissionId }).ConfigureAwait(false);
            DecodeCacheEvict(DecodeKey(playerId, row.MissionId));
        }

        var dropRows = await db.GetAllByIndexAsync<ArtifactDropRow>(IndexedDbStores.ArtifactDrops, IndexedDbStores.PlayerIdIndex, playerId).ConfigureAwait(false);
        foreach (var row in dropRows) {
            if (row.Id is { } id) {
                await db.DeleteAsync(IndexedDbStores.ArtifactDrops, id).ConfigureAwait(false);
            }
        }

        var fuelRows = await db.GetAllByIndexAsync<FuelRow>(IndexedDbStores.MissionFuel, IndexedDbStores.PlayerIdIndex, playerId).ConfigureAwait(false);
        foreach (var row in fuelRows) {
            if (row.Id is { } id) {
                await db.DeleteAsync(IndexedDbStores.MissionFuel, id).ConfigureAwait(false);
            }
        }

        var inFlightRows = await db.GetAllByIndexAsync<InFlightMissionRow>(IndexedDbStores.InFlightMission, IndexedDbStores.PlayerIdIndex, playerId).ConfigureAwait(false);
        foreach (var row in inFlightRows) {
            await db.DeleteAsync(IndexedDbStores.InFlightMission, new object[] { playerId, row.MissionId }).ConfigureAwait(false);
        }

        await db.DeleteAsync(IndexedDbStores.Backup, playerId).ConfigureAwait(false);
    }

    private async Task<CompleteMissionResponse> DecodeAsync(MissionRow row) {
        byte[] raw = Gunzip(row.CompletePayload);
        var resp = await decoder.DecodeCompleteMissionAsync(raw).ConfigureAwait(false);
        resp.Info?.StartTimeDerived = row.StartTimestamp;
        return resp;
    }

    private static byte[] Gunzip(byte[] data) {
        using var input = new MemoryStream(data, writable: false);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }

    private static MissionMeta ToMeta(MissionMetaRow row) => new() {
        MissionId = row.MissionId,
        StartTimestamp = row.StartTimestamp,
        ReturnTimestamp = row.ReturnTimestamp,
        Ship = row.Ship,
        DurationType = row.DurationType,
        Level = row.Level,
        Capacity = row.Capacity,
        NominalCapacity = row.NominalCapacity,
        IsDubCap = row.IsDubCap,
        IsBuggedCap = row.IsBuggedCap,
        Target = row.Target,
        MissionType = row.MissionType,
    };
}
