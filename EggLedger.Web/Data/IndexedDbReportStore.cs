using System.Text.Json;

namespace EggLedger.Web.Data;

public sealed class IndexedDbReportStore(IIndexedDb db, TimeProvider time) {
    private const string GlobalAccountId = "__global__";
    private static readonly JsonWriterOptions CompactWriter = new() { Indented = false };

    private long Now() => time.GetUtcNow().ToUnixTimeSeconds();

    public static string NormalizeFiltersJson(string? s) {
        if (string.IsNullOrWhiteSpace(s)) {
            return """{"and":[],"or":[]}""";
        }
        using var doc = JsonDocument.Parse(s);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, CompactWriter)) {
            doc.RootElement.WriteTo(writer);
        }
        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    public static string NormalizeByOrDefault(string? v) =>
        string.IsNullOrEmpty(v) ? Domain.Reports.ReportDefaults.NormalizeNone : v;

    public async Task InsertReportAsync(ReportRow r) {
        long stamp = Now();
        var row = r with {
            Filters = NormalizeFiltersJson(r.Filters),
            NormalizeBy = NormalizeByOrDefault(r.NormalizeBy),
            CreatedAt = stamp,
            UpdatedAt = stamp,
        };
        await db.PutAsync(IndexedDbStores.Reports, row);
    }

    public async Task UpdateReportAsync(ReportRow r) {
        long createdAt = r.CreatedAt;
        var existing = await db.GetAsync<ReportRow>(IndexedDbStores.Reports, r.Id);
        if (existing is not null) {
            createdAt = existing.CreatedAt;
        }
        var row = r with {
            Filters = NormalizeFiltersJson(r.Filters),
            NormalizeBy = NormalizeByOrDefault(r.NormalizeBy),
            CreatedAt = createdAt,
            UpdatedAt = Now(),
        };
        await db.PutAsync(IndexedDbStores.Reports, row);
    }

    public Task DeleteReportAsync(string id) =>
        db.DeleteAsync(IndexedDbStores.Reports, id).AsTask();

    public async Task<ReportRow?> RetrieveReportAsync(string id) =>
        await db.GetAsync<ReportRow>(IndexedDbStores.Reports, id);

    public async Task<IReadOnlyList<ReportRow>> RetrieveAccountReportsAsync(string accountId) {
        var owned = await db.GetAllByIndexAsync<ReportRow>(IndexedDbStores.Reports, IndexedDbStores.AccountIdIndex, accountId);
        var global = accountId == GlobalAccountId
            ? []
            : await db.GetAllByIndexAsync<ReportRow>(IndexedDbStores.Reports, IndexedDbStores.AccountIdIndex, GlobalAccountId);
        return [.. owned.Concat(global)
            .OrderBy(r => r.SortOrder)
            .ThenBy(r => r.CreatedAt)];
    }

    public async Task ReorderReportsAsync(IReadOnlyList<string> ids) {
        for (int i = 0; i < ids.Count; i++) {
            var row = await db.GetAsync<ReportRow>(IndexedDbStores.Reports, ids[i]);
            if (row is null) {
                continue;
            }
            await db.PutAsync(IndexedDbStores.Reports, row with { SortOrder = i });
        }
    }

    public async Task<string> InsertReportGroupAsync(ReportGroupRow r) {
        string id = string.IsNullOrEmpty(r.Id) ? Guid.NewGuid().ToString("D") : r.Id;
        var row = r with { Id = id, CreatedAt = Now() };
        await db.PutAsync(IndexedDbStores.ReportGroups, row);
        return id;
    }

    public async Task UpdateReportGroupAsync(ReportGroupRow r) {
        var existing = await db.GetAsync<ReportGroupRow>(IndexedDbStores.ReportGroups, r.Id);

        if (existing is null) {
            return;
        }
        await db.PutAsync(IndexedDbStores.ReportGroups, existing with { Name = r.Name, SortOrder = r.SortOrder });
    }

    public async Task DeleteReportGroupAsync(string id) {
        var members = await RetrieveReportsByGroupAsync(id);
        foreach (var m in members) {
            await db.PutAsync(IndexedDbStores.Reports, m with { GroupId = "" });
        }
        await db.DeleteAsync(IndexedDbStores.ReportGroups, id);
    }

    public async Task DeleteAllForAccountAsync(string accountId) {
        var reports = await db.GetAllByIndexAsync<ReportRow>(IndexedDbStores.Reports, IndexedDbStores.AccountIdIndex, accountId);
        foreach (var r in reports) {
            await db.DeleteAsync(IndexedDbStores.Reports, r.Id);
        }

        var groups = await db.GetAllByIndexAsync<ReportGroupRow>(IndexedDbStores.ReportGroups, IndexedDbStores.AccountIdIndex, accountId);
        foreach (var g in groups) {
            await db.DeleteAsync(IndexedDbStores.ReportGroups, g.Id);
        }
    }

    public async Task<IReadOnlyList<ReportGroupRow>> RetrieveAccountGroupsAsync(string accountId) {
        var rows = await db.GetAllByIndexAsync<ReportGroupRow>(IndexedDbStores.ReportGroups, IndexedDbStores.AccountIdIndex, accountId);
        return [.. rows.OrderBy(r => r.SortOrder).ThenBy(r => r.CreatedAt)];
    }

    public async Task<ReportGroupRow?> RetrieveReportGroupAsync(string id) =>
        await db.GetAsync<ReportGroupRow>(IndexedDbStores.ReportGroups, id);

    public async Task<IReadOnlyList<ReportRow>> RetrieveReportsByGroupAsync(string groupId) {
        var all = await db.GetAllAsync<ReportRow>(IndexedDbStores.Reports);
        return [.. all.Where(r => r.GroupId == groupId)
            .OrderBy(r => r.SortOrder)
            .ThenBy(r => r.CreatedAt)];
    }

    public async Task SetReportGroupAsync(string reportId, string groupId) {
        var row = await db.GetAsync<ReportRow>(IndexedDbStores.Reports, reportId);
        if (row is null) {
            return;
        }
        await db.PutAsync(IndexedDbStores.Reports, row with { GroupId = groupId });
    }
}
