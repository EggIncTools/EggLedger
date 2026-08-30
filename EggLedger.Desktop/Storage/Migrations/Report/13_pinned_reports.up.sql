CREATE TABLE IF NOT EXISTS pinned_reports (
    id         TEXT    PRIMARY KEY,
    account_id TEXT    NOT NULL,
    view       TEXT    NOT NULL,
    kind       TEXT    NOT NULL,
    ref_id     TEXT    NOT NULL,
    sort_order INTEGER NOT NULL DEFAULT 0,
    created_at INTEGER NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_pinned_reports_account_view ON pinned_reports(account_id, view);
