namespace EggLedger.Web.State;

public sealed class ActiveAccount {
    public string? ActiveAccountId { get; private set; }
    public event Action? Changed;

    public void SetActive(string? id) {
        var normalized = string.IsNullOrEmpty(id) ? null : id;
        if (normalized == ActiveAccountId) return;

        ActiveAccountId = normalized;
        Changed?.Invoke();
    }
}
