using EggLedger.Domain.Reports;
using EggLedger.Web.Data;

namespace EggLedger.Web.State;

public interface IReportsViewActions {
    Task NewReportAsync();

    Task ExportAllAsync();

    Task ImportAsync();
}

public sealed class ReportsViewState(ActiveAccount active, IndexedDbReportStore store) : IDisposable {
    private Func<Func<Task>, Task>? _dispatch;
    private bool _initialized;
    private string _loadedAccount = "";
    private int _loadGeneration;
    private int _reportsLoadGeneration;

    public event Action? Changed;

    public IReadOnlyList<ReportGroupRow> Groups { get; private set; } = [];

    public IReadOnlyList<ReportDefinition> Reports { get; private set; } = [];

    public string? SelectedGroup { get; private set; }

    public bool EditMode { get; private set; }

    public bool ShowNewGroup { get; private set; }

    public string NewGroupName { get; set; } = "";

    public string RenameGroupName { get; set; } = "";

    public string? RenamingGroupId { get; private set; }

    public string? AccountId => active.ActiveAccountId;

    public IReportsViewActions? Actions { get; set; }

    public Task RequestNewReportAsync() {
        return Actions?.NewReportAsync() ?? Task.CompletedTask;
    }

    public Task RequestExportAllAsync() {
        return Actions?.ExportAllAsync() ?? Task.CompletedTask;
    }

    public Task RequestImportAsync() {
        return Actions?.ImportAsync() ?? Task.CompletedTask;
    }

    public async Task EnsureInitializedAsync(Func<Func<Task>, Task> dispatch) {
        if (_initialized) {
            return;
        }

        _initialized = true;
        _dispatch = dispatch;
        active.Changed += OnActiveChanged;
        await EnsureAccountLoadedAsync();
    }

    public async Task EnsureAccountLoadedAsync() {
        var id = active.ActiveAccountId;
        if (id is not null && id != _loadedAccount) {
            _loadedAccount = id;
            SelectedGroup = null;
            await LoadGroupsAsync();
            await LoadReportsAsync();
        }
    }

    public async Task<bool> LoadReportsAsync() {
        var generation = ++_reportsLoadGeneration;
        var id = active.ActiveAccountId;
        if (id is null) {
            Reports = [];
            return true;
        }

        var rows = await store.RetrieveAccountReportsAsync(id);
        if (generation != _reportsLoadGeneration) {
            return false;
        }

        Reports = rows.Select(ReportMapping.ToDefinition).ToList();
        Changed?.Invoke();
        return true;
    }

    public async Task<bool> LoadGroupsAsync() {
        var generation = ++_loadGeneration;
        var id = active.ActiveAccountId;
        if (id is null) {
            Groups = [];
            return true;
        }

        var groups = await store.RetrieveAccountGroupsAsync(id);
        if (generation != _loadGeneration) {
            return false;
        }

        Groups = groups;
        Changed?.Invoke();
        return true;
    }

    public void SelectGroup(string? id) {
        SelectedGroup = id;
        Changed?.Invoke();
    }

    public void ToggleEditMode() {
        EditMode = !EditMode;
        Changed?.Invoke();
    }

    public void BeginNewGroup() {
        ShowNewGroup = true;
        Changed?.Invoke();
    }

    public void CancelNewGroup() {
        ShowNewGroup = false;
        NewGroupName = "";
        Changed?.Invoke();
    }

    public async Task CreateGroupAsync() {
        var id = active.ActiveAccountId;
        var name = NewGroupName.Trim();
        ShowNewGroup = false;
        NewGroupName = "";
        if (id is null || string.IsNullOrEmpty(name)) {
            Changed?.Invoke();
            return;
        }

        var groupId = await store.InsertReportGroupAsync(new ReportGroupRow { AccountId = id, Name = name });
        if (await LoadGroupsAsync()) {
            SelectedGroup = groupId;
        }

        Changed?.Invoke();
    }

    public async Task DeleteGroupAsync(string id) {
        await store.DeleteReportGroupAsync(id);
        if (SelectedGroup == id) {
            SelectedGroup = null;
        }

        await LoadGroupsAsync();
    }

    public void StartRename(ReportGroupRow group) {
        RenamingGroupId = group.Id;
        RenameGroupName = group.Name;
        Changed?.Invoke();
    }

    public void CancelRename() {
        RenamingGroupId = null;
        Changed?.Invoke();
    }

    public async Task CommitRenameAsync(string groupId) {
        if (RenamingGroupId != groupId) {
            return;
        }

        RenamingGroupId = null;
        var name = RenameGroupName.Trim();
        var existing = Groups.FirstOrDefault(g => g.Id == groupId);
        if (existing is null || name.Length == 0 || name == existing.Name) {
            Changed?.Invoke();
            return;
        }

        await store.UpdateReportGroupAsync(existing with { Name = name });
        await LoadGroupsAsync();
    }

    public void Dispose() {
        active.Changed -= OnActiveChanged;
    }

    private void OnActiveChanged() {
        Changed?.Invoke();
        _ = _dispatch?.Invoke(EnsureAccountLoadedAsync);
    }
}
