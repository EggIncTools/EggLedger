CREATE TABLE IF NOT EXISTS el_pinned_reports (
    user_id    UUID    NOT NULL,
    id         TEXT    NOT NULL,
    account_id TEXT    NOT NULL,
    view       TEXT    NOT NULL,
    kind       TEXT    NOT NULL,
    ref_id     TEXT    NOT NULL,
    sort_order BIGINT  NOT NULL DEFAULT 0,
    created_at BIGINT  NOT NULL,
    PRIMARY KEY (user_id, id)
);
CREATE INDEX IF NOT EXISTS idx_el_pinned_reports_user_account ON el_pinned_reports(user_id, account_id);
