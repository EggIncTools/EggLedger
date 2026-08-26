using EggLedger.Desktop.Storage;
using EggLedger.Domain.Export;
using EggLedger.Domain.MissionQuery;
using EggLedger.Web.Platform;

namespace EggLedger.Desktop.Export;

public sealed class DesktopExportService(
    string dataRootDir,
    IExportFileSystem? fs = null,
    Func<Task<IReadOnlyList<AccountInfo>>>? accountLookup = null) : IExportManagement {

    private readonly string _exportsDir = StoragePaths.ResolveExportsDir(dataRootDir);
    private readonly IExportFileSystem _fs = fs ?? new PhysicalExportFileSystem();

    public async Task<List<ExportGroup>> ListAsync() {
        var dir = _exportsDir;
        var groups = await Task.Run(() => ExportManagement.ListGroups(dir, _fs));
        if (accountLookup is null || groups.Count == 0) {
            return groups;
        }
        var accounts = await accountLookup();
        var byId = new Dictionary<string, AccountInfo>(StringComparer.Ordinal);
        foreach (var a in accounts) {
            byId[a.Id] = a;
        }
        foreach (var g in groups) {
            if (byId.TryGetValue(g.Eid, out var acct)) {
                g.Nickname = acct.Nickname;
                g.AccountColor = acct.AccountColor;
            }
        }
        return groups;
    }

    public Task<(int deleted, long freedBytes)> PruneAsync(int keepCount) {
        var dir = _exportsDir;
        return Task.Run(() => {
            int deleted = 0;
            long freed = 0;
            foreach (var g in ExportManagement.ListGroups(dir, _fs)) {
                var (d, f) = ExportManagement.PruneForPlayer(dir, g.Eid, keepCount, _fs);
                deleted += d;
                freed += f;
            }
            return (deleted, freed);
        });
    }

    public Task DeleteAsync(IEnumerable<string> paths) {
        var snapshot = paths.ToList();
        return Task.Run(() => {
            foreach (var path in snapshot) {
                if (!string.IsNullOrEmpty(path)) {
                    _fs.Delete(path);
                }
            }
        });
    }
}
