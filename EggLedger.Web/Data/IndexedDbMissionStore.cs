using System.IO.Compression;
using System.Text.Json;
using EggLedger.Domain.Api;
using EggLedger.Domain.MissionPacking;
using EggLedger.Domain.MissionQuery;
using Ei;
using Microsoft.Extensions.Logging;

namespace EggLedger.Web.Data;

public sealed class IndexedDbMissionStore : IMissionStore {
    private readonly IIndexedDb _db;
    private readonly MissionPacker _packer;
    private readonly IApiPayloadDecoder _decoder;
    private readonly IndexedDbAccountStore? _accounts;
    private readonly ILogger<IndexedDbMissionStore>? _logger;

    public IndexedDbMissionStore(IIndexedDb db, IApiPayloadDecoder decoder, MissionPacker? packer = null, IndexedDbAccountStore? accounts = null, ILogger<IndexedDbMissionStore>? logger = null) {
        _db = db;
        _decoder = decoder;
        _packer = packer ?? new MissionPacker(EiafxMissionConfigSource.Instance);
        _accounts = accounts;
        _logger = logger;
    }

    public async Task<IReadOnlyList<string>?> GetCompleteMissionIdsAsync(string playerId) {
        var rows = await PlayerMetaRowsAsync(playerId);
        return rows.OrderBy(r => r.StartTimestamp)
            .Select(r => r.MissionId)
            .ToList();
    }

    public async Task<IReadOnlyList<KnownAccount>> GetKnownAccountsAsync() {
        if (_accounts is null) {
            return [];
        }
        var accounts = await _accounts.GetKnownAccountsAsync().ConfigureAwait(false);
        return accounts.Select(a => a.ToKnownAccount()).ToList();
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
            } catch {
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
        var row = await _db.GetAsync<MissionRow>(IndexedDbStores.Mission, new object[] { playerId, missionId }).ConfigureAwait(false);
        if (row is null) {
            return null;
        }
        try {
            var decoded = await DecodeAsync(row).ConfigureAwait(false);
            DecodeCachePut(key, decoded);
            return decoded;
        } catch {
            return null;
        }
    }

    public async Task<int?> CountPendingFilterColsAsync(string eid) {
        var rows = await PlayerMetaRowsAsync(eid);
        return rows.Count(r => r.Ship == -1);
    }

    public async Task<IReadOnlyList<IMissionRow>?> GetPlayerMissionMetaAsync(string eid) {
        var rows = await PlayerMetaRowsAsync(eid);
        var result = new List<IMissionRow>();
        foreach (var row in rows.Where(r => r.Ship != -1).OrderBy(r => r.StartTimestamp)) {
            result.Add(_packer.MissionMetaToDBMission(ToMeta(row)));
        }
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
                for (int i = start; i < end; i++) {
                    int idx = i;
                    tasks[idx - start] = Assign(idx);
                }
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            return result;

            async Task Assign(int idx) => result[idx] = await DecodeAsync(rows[idx]).ConfigureAwait(false);
        } catch {
            return null;
        }
    }



    private const int DecodeCacheCap = 256;
    private readonly Lock _decodeGate = new();
    private readonly LinkedList<(string Key, CompleteMissionResponse Value)> _decodeLru = new();
    private readonly Dictionary<string, LinkedListNode<(string Key, CompleteMissionResponse Value)>> _decodeIndex = new(StringComparer.Ordinal);

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

    private readonly HashSet<string> _backfilling = [];




    public void QueueFilterColBackfill(string eid) {
        if (_backfilling.Add(eid))
            _ = BackfillFilterColsAsync(eid);
    }

    private async Task BackfillFilterColsAsync(string eid) {
        try {
            foreach (var row in await PlayerRowsAsync(eid).ConfigureAwait(false)) {
                if (row.Ship != -1)
                    continue;

                CompleteMissionResponse decoded;
                try { decoded = await DecodeAsync(row).ConfigureAwait(false); } catch { continue; }

                if (_packer.TryComputeMissionFilterCols(row.StartTimestamp, decoded, out var cols)) {
                    await _db.PutAsync(IndexedDbStores.Mission, WithCols(row, cols)).ConfigureAwait(false);
                    DecodeCacheEvict(DecodeKey(eid, row.MissionId));
                }
            }
        } finally {
            _backfilling.Remove(eid);
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
                try { decoded = await GetCompleteMissionAsync(playerId, missionId).ConfigureAwait(false) ?? throw new InvalidOperationException(); } catch { continue; }

                var drops = ArtifactDrops.Build(decoded);
                if (drops.Count == 0) {
                    await _db.PutAsync(IndexedDbStores.ArtifactDrops, new ArtifactDropRow {
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
                await _db.PutManyAsync(IndexedDbStores.ArtifactDrops, rows).ConfigureAwait(false);
            }
        } finally {
            _dropsBackfilling.Remove(playerId);
        }
    }

    public async Task InsertBackupAsync(string playerId, double timestamp, byte[] rawPayload, TimeSpan minimumGap) {
        if (minimumGap > TimeSpan.Zero) {
            var existing = await _db.GetAsync<BackupRow>(IndexedDbStores.Backup, playerId);
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
        await _db.PutAsync(IndexedDbStores.Backup, row);
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
        await _db.PutAsync(IndexedDbStores.Mission, row);
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
            await _db.PutManyAsync(IndexedDbStores.ArtifactDrops, rows);
        }
    }

    public async Task<bool> ReplaceInFlightMissionsAsync(string playerId, IReadOnlyList<DatabaseMission> missions) {
        long capturedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
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
                await _db.PutManyAsync(IndexedDbStores.InFlightMission, rows).ConfigureAwait(false);
            }

            var stored = await _db
                .GetAllByIndexAsync<InFlightMissionRow>(IndexedDbStores.InFlightMission, IndexedDbStores.PlayerIdIndex, playerId)
                .ConfigureAwait(false);
            foreach (var stale in stored.Select(r => r.MissionId).Where(id => !keep.Contains(id))) {
                await _db.DeleteAsync(
                    IndexedDbStores.InFlightMission, new object[] { playerId, stale }).ConfigureAwait(false);
            }
            return true;
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "failed to persist in-flight missions for player {PlayerId}", playerId);
            return false;
        }
    }

    public async Task<IReadOnlyList<DatabaseMission>> GetInFlightMissionsAsync(string playerId) {
        InFlightMissionRow[] rows;
        try {
            rows = await _db
                .GetAllByIndexAsync<InFlightMissionRow>(IndexedDbStores.InFlightMission, IndexedDbStores.PlayerIdIndex, playerId)
                .ConfigureAwait(false);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "failed to read in-flight missions for player {PlayerId}", playerId);
            return [];
        }

        var result = new List<DatabaseMission>(rows.Length);
        foreach (var row in rows) {
            DatabaseMission? mission;
            try {
                mission = JsonSerializer.Deserialize<DatabaseMission>(row.Payload, Rows.JsonOptions);
            } catch (JsonException) {
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
        var rows = await _db.GetAllByIndexAsync<MissionRow>(IndexedDbStores.Mission, IndexedDbStores.PlayerIdIndex, playerId);
        return [.. rows];
    }


    private async Task<List<MissionMetaRow>> PlayerMetaRowsAsync(string playerId) {
        var rows = await _db.GetAllByIndexProjectedAsync<MissionMetaRow>(IndexedDbStores.Mission, IndexedDbStores.PlayerIdIndex, playerId);
        return [.. rows];
    }

    public async Task<IReadOnlyList<StoredDrop>?> GetStoredPlayerDropsAsync(string playerId) {
        try {
            var rows = await _db.GetAllByIndexAsync<ArtifactDropRow>(IndexedDbStores.ArtifactDrops, IndexedDbStores.PlayerIdIndex, playerId);
            return [.. rows.Select(r => new StoredDrop(r.MissionId, r.ArtifactId, r.Level, r.Rarity, r.DropIndex))];
        } catch {
            return null;
        }
    }

    private async Task<CompleteMissionResponse> DecodeAsync(MissionRow row) {
        byte[] raw = Gunzip(row.CompletePayload);
        var resp = await _decoder.DecodeCompleteMissionAsync(raw).ConfigureAwait(false);
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
