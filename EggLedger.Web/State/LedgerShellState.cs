namespace EggLedger.Web.State;

public enum LedgerView {
    Ships,
    Drops,
    Reports
}

public enum LegalDocument {
    Terms,
    Privacy
}

public sealed class LedgerShellState {
    public event Action? Changed;

    public event Action? AccountPopoverRequested;

    public LedgerView View { get; private set; }

    public bool SettingsOpen {
        get;
        set {
            if (field == value) {
                return;
            }

            field = value;
            Changed?.Invoke();
        }
    }

    public bool AboutOpen {
        get;
        set {
            if (field == value) {
                return;
            }

            field = value;
            Changed?.Invoke();
        }
    }

    public LegalDocument? Legal {
        get;
        set {
            if (field == value) {
                return;
            }

            field = value;
            Changed?.Invoke();
        }
    }

    public bool SupportOpen {
        get;
        set {
            if (field == value) {
                return;
            }

            field = value;
            Changed?.Invoke();
        }
    }

    public void CloseModals() {
        SettingsOpen = false;
        AboutOpen = false;
        Legal = null;
        SupportOpen = false;
    }

    public void RequestAccountPopover() {
        AccountPopoverRequested?.Invoke();
    }

    public void SetView(LedgerView view) {
        if (View == view) {
            return;
        }

        View = view;
        Changed?.Invoke();
    }
}
