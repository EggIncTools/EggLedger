namespace EggLedger.Web.State;

public sealed class AdminState {
    public bool IsAdmin {
        get;
        set {
            if (field == value) return;
            field = value;
            Changed?.Invoke();
        }
    }

    public event Action? Changed;
}
