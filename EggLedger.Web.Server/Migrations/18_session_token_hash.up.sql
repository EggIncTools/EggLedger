-- Bearer session tokens were stored raw: sessions.token held the value the client presents,
-- as the primary key. Replace it with a SHA-256 hex hash, matching how EggIncognito stores
-- api_keys.key_hash. Existing rows cannot be converted, because the raw token is exactly what
-- is being removed and it cannot be recovered from anything else, so they are deleted and
-- every current /api/v1 device client must re-pair.
--
-- expires_at on both tables moves from a bigint unix epoch to timestamptz so the sweeper added
-- alongside this migration can compare against now() directly.

DELETE FROM sessions;
DELETE FROM pending_auth;

ALTER TABLE sessions DROP CONSTRAINT IF EXISTS sessions_pkey;
ALTER TABLE sessions DROP COLUMN IF EXISTS token;
ALTER TABLE sessions ADD COLUMN IF NOT EXISTS token_hash TEXT NOT NULL;

DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = 'sessions'::regclass AND contype = 'p') THEN
        ALTER TABLE sessions ADD PRIMARY KEY (token_hash);
    END IF;
END $$;

ALTER TABLE sessions
    ALTER COLUMN expires_at TYPE TIMESTAMPTZ USING to_timestamp(expires_at);

ALTER TABLE pending_auth
    ALTER COLUMN expires_at TYPE TIMESTAMPTZ USING to_timestamp(expires_at);

CREATE INDEX IF NOT EXISTS idx_sessions_expires_at ON sessions(expires_at);
CREATE INDEX IF NOT EXISTS idx_pending_auth_expires_at ON pending_auth(expires_at);
