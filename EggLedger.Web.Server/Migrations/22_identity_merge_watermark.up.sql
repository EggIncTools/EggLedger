CREATE TABLE IF NOT EXISTS identity_merge_watermark (
    id        BOOLEAN     PRIMARY KEY DEFAULT TRUE CHECK (id),
    merged_at TIMESTAMPTZ NOT NULL
);
