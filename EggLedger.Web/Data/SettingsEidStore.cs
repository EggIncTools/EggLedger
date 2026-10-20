using System.Text.Json;
using EggIdentity.Contract;
using EggIdentity.UI.Suite;

namespace EggLedger.Web.Data;

public sealed class SettingsEidStore(IndexedDbSettings settings, TimeProvider? time = null) : ISuiteEidStore, IDisposable {
    internal const string EidsKey = "suite_eids";
    internal const string ActiveKey = "suite_active_eid";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public bool IsAvailable => true;

    public async Task<ProfileGame?> LoadAsync(CancellationToken ct) {
        var all = await settings.GetAllSettingsAsync().ConfigureAwait(false);
        var active = all.TryGetValue(ActiveKey, out var id) && !string.IsNullOrEmpty(id) ? id : null;
        var eids = Parse(all);
        return new ProfileGame {
            ActiveEid = active,
            ActiveEidLabel = eids.FirstOrDefault(e => e.Eid == active)?.Name,
            Eids = eids,
        };
    }

    public async Task<bool> SetActiveAsync(string? eid, string? label, CancellationToken ct) {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try {
            var list = await ReadAsync().ConfigureAwait(false);
            if (eid is not null && list.All(e => e.Eid != eid)) {
                if (!EidRules.IsValid(eid) || list.Count >= EidRules.MaxEntries) return false;
                var normalized = EidRules.NormalizeLabel(label);
                list.Add(new ProfileEid { Eid = eid, Label = normalized?.Length > EidRules.MaxLabelLength ? null : normalized });
                await WriteAsync(list).ConfigureAwait(false);
            }
            await settings.SetSettingAsync(ActiveKey, eid ?? "").ConfigureAwait(false);
            return true;
        } finally {
            _gate.Release();
        }
    }

    public async Task<EidUpsertResult?> UpsertAsync(IReadOnlyList<EidUpsert> entries, CancellationToken ct) {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try {
            var list = await ReadAsync().ConfigureAwait(false);
            var rejected = new List<EidRejection>();
            foreach (var entry in entries) {
                var eid = entry.Eid.Trim();
                var label = EidRules.NormalizeLabel(entry.Label);
                var index = list.FindIndex(e => e.Eid == eid);
                if (Reject(eid, label, index < 0 && list.Count >= EidRules.MaxEntries) is { } reason) {
                    rejected.Add(new EidRejection { Eid = eid, Reason = reason });
                } else if (index < 0) {
                    list.Add(new ProfileEid { Eid = eid, Label = label, Snapshot = entry.Snapshot, SnapshotAt = entry.Snapshot is null ? null : _time.GetUtcNow() });
                } else if (entry.Snapshot is not null) {
                    list[index] = With(list[index], list[index].Label, entry.Snapshot);
                }
            }
            await WriteAsync(list).ConfigureAwait(false);
            return new EidUpsertResult { Eids = list, Rejected = rejected };
        } finally {
            _gate.Release();
        }
    }

    public Task<bool> UpdateSnapshotAsync(string eid, EidSnapshot snapshot, CancellationToken ct) =>
        MutateAsync(list => {
            var index = list.FindIndex(e => e.Eid == eid);
            if (index < 0) return false;
            list[index] = With(list[index], list[index].Label, snapshot);
            return true;
        }, ct);

    public Task<bool> RenameAsync(string eid, string? label, CancellationToken ct) =>
        MutateAsync(list => {
            var normalized = EidRules.NormalizeLabel(label);
            var index = list.FindIndex(e => e.Eid == eid);
            if (index < 0 || normalized?.Length > EidRules.MaxLabelLength) return false;
            list[index] = new ProfileEid { Eid = eid, Label = normalized, Snapshot = list[index].Snapshot, SnapshotAt = list[index].SnapshotAt };
            return true;
        }, ct);

    public async Task<bool> RemoveAsync(string eid, CancellationToken ct) {
        var removed = await MutateAsync(list => list.RemoveAll(e => e.Eid == eid) > 0, ct).ConfigureAwait(false);
        if (!removed) return false;
        var all = await settings.GetAllSettingsAsync().ConfigureAwait(false);
        if (all.TryGetValue(ActiveKey, out var active) && active == eid) await settings.SetSettingAsync(ActiveKey, "").ConfigureAwait(false);
        return true;
    }

    public Task<bool> ReorderAsync(IReadOnlyList<string> eids, CancellationToken ct) =>
        MutateAsync(list => {
            if (eids.Count != list.Count || eids.Distinct(StringComparer.Ordinal).Count() != list.Count) return false;
            var byId = list.ToDictionary(e => e.Eid, StringComparer.Ordinal);
            if (!eids.All(byId.ContainsKey)) return false;
            var ordered = eids.Select(id => byId[id]).ToList();
            list.Clear();
            list.AddRange(ordered);
            return true;
        }, ct);

    private async Task<bool> MutateAsync(Func<List<ProfileEid>, bool> change, CancellationToken ct) {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try {
            var list = await ReadAsync().ConfigureAwait(false);
            if (!change(list)) return false;
            await WriteAsync(list).ConfigureAwait(false);
            return true;
        } finally {
            _gate.Release();
        }
    }

    private ProfileEid With(ProfileEid entry, string? label, EidSnapshot snapshot) =>
        new() { Eid = entry.Eid, Label = label, Snapshot = snapshot, SnapshotAt = _time.GetUtcNow() };

    private async Task<List<ProfileEid>> ReadAsync() =>
        Parse(await settings.GetAllSettingsAsync().ConfigureAwait(false));

    private Task WriteAsync(List<ProfileEid> list) =>
        settings.SetSettingAsync(EidsKey, JsonSerializer.Serialize(list, Json));

    public void Dispose() => _gate.Dispose();

    private static string? Reject(string eid, string? label, bool full) {
        if (!EidRules.IsValid(eid)) return EidRules.FormatReason;
        if (label?.Length > EidRules.MaxLabelLength) return EidRules.LabelReason;
        return full ? EidRules.CapReason : null;
    }

    private static List<ProfileEid> Parse(Dictionary<string, string> all) {
        if (!all.TryGetValue(EidsKey, out var raw) || string.IsNullOrEmpty(raw)) return [];
        try {
            return JsonSerializer.Deserialize<List<ProfileEid>>(raw, Json) ?? [];
        } catch (JsonException) {
            return [];
        }
    }
}
