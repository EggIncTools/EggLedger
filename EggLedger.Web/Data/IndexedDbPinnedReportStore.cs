namespace EggLedger.Web.Data;

public sealed class IndexedDbPinnedReportStore(IIndexedDb db, Func<long>? now = null) {
    private readonly IIndexedDb _db = db;
    private readonly Func<long> _now = now ?? (() => DateTimeOffset.UtcNow.ToUnixTimeSeconds());

    public async Task InsertAsync(PinnedReportRow r) {
        var row = r with {
            Id = string.IsNullOrEmpty(r.Id) ? Guid.NewGuid().ToString("D") : r.Id,
            CreatedAt = _now(),
        };
        await _db.PutAsync(IndexedDbStores.PinnedReports, row);
    }

    public Task DeleteAsync(string id) =>
        _db.DeleteAsync(IndexedDbStores.PinnedReports, id).AsTask();

    public async Task<IReadOnlyList<PinnedReportRow>> RetrieveAsync(string accountId, string view) {
        var rows = await _db.GetAllByIndexAsync<PinnedReportRow>(IndexedDbStores.PinnedReports, IndexedDbStores.AccountIdIndex, accountId);
        return rows.Where(r => r.View == view)
            .OrderBy(r => r.SortOrder)
            .ThenBy(r => r.CreatedAt)
            .ToList();
    }

    public async Task ReorderAsync(string accountId, string view, IReadOnlyList<string> ids) {
        var current = await RetrieveAsync(accountId, view);
        var byId = current.ToDictionary(r => r.Id);
        for (int i = 0; i < ids.Count; i++) {
            if (byId.TryGetValue(ids[i], out var row)) {
                await _db.PutAsync(IndexedDbStores.PinnedReports, row with { SortOrder = i });
            }
        }
    }
}
