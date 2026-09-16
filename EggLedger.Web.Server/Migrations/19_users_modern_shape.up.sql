-- Brings `users` to the shape EggIdentity's own users table already has, so the two converge:
-- created_at to timestamptz, plus role/timezone/language/theme/last_login_at, and avatar_url
-- retired in favour of avatar + avatar_is_custom. Column types are copied from EggIdentity
-- migrations 1, 6 and 9 rather than invented.
--
-- Epoch columns are converted only where the server owns the value. el_backup.backed_up_at,
-- el_inflight_mission.captured_at and the el_reports/el_report_groups/el_pinned_reports
-- created_at columns are deliberately left as bigint/double: they round-trip through
-- JsonRowCodec, which is shared with the desktop host's SqliteIndexedDb, and SQLite has no
-- timestamptz. Converting them desynchronizes the two hosts' wire format for the same store,
-- so it is a cross-host change needing the desktop host in scope, not a column retype.

ALTER TABLE users
    ALTER COLUMN created_at TYPE TIMESTAMPTZ USING to_timestamp(created_at);
ALTER TABLE users
    ALTER COLUMN created_at SET DEFAULT now();

ALTER TABLE users ADD COLUMN IF NOT EXISTS role TEXT NOT NULL DEFAULT 'viewer';
ALTER TABLE users ADD COLUMN IF NOT EXISTS timezone TEXT;
ALTER TABLE users ADD COLUMN IF NOT EXISTS language TEXT;
ALTER TABLE users ADD COLUMN IF NOT EXISTS theme JSONB;
ALTER TABLE users ADD COLUMN IF NOT EXISTS last_login_at TIMESTAMPTZ NOT NULL DEFAULT now();
ALTER TABLE users ADD COLUMN IF NOT EXISTS avatar TEXT;
ALTER TABLE users ADD COLUMN IF NOT EXISTS avatar_is_custom BOOLEAN NOT NULL DEFAULT false;

UPDATE users SET avatar = NULLIF(avatar_url, '') WHERE avatar IS NULL;
ALTER TABLE users DROP COLUMN IF EXISTS avatar_url;

ALTER TABLE pending_auth RENAME COLUMN avatar_url TO avatar;

ALTER TABLE blobs
    ALTER COLUMN updated_at TYPE TIMESTAMPTZ USING to_timestamp(updated_at);
ALTER TABLE blobs
    ALTER COLUMN updated_at SET DEFAULT now();

ALTER TABLE el_api_spam
    ALTER COLUMN first_seen TYPE TIMESTAMPTZ USING to_timestamp(first_seen);
ALTER TABLE el_api_spam
    ALTER COLUMN last_seen TYPE TIMESTAMPTZ USING to_timestamp(last_seen);

DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_el_mission_user_id') THEN
        ALTER TABLE el_mission ADD CONSTRAINT fk_el_mission_user_id
            FOREIGN KEY (user_id) REFERENCES users(user_id) ON UPDATE CASCADE ON DELETE CASCADE NOT VALID;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_el_artifact_drops_user_id') THEN
        ALTER TABLE el_artifact_drops ADD CONSTRAINT fk_el_artifact_drops_user_id
            FOREIGN KEY (user_id) REFERENCES users(user_id) ON UPDATE CASCADE ON DELETE CASCADE NOT VALID;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_el_backup_user_id') THEN
        ALTER TABLE el_backup ADD CONSTRAINT fk_el_backup_user_id
            FOREIGN KEY (user_id) REFERENCES users(user_id) ON UPDATE CASCADE ON DELETE CASCADE NOT VALID;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_el_mission_fuel_user_id') THEN
        ALTER TABLE el_mission_fuel ADD CONSTRAINT fk_el_mission_fuel_user_id
            FOREIGN KEY (user_id) REFERENCES users(user_id) ON UPDATE CASCADE ON DELETE CASCADE NOT VALID;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_el_settings_user_id') THEN
        ALTER TABLE el_settings ADD CONSTRAINT fk_el_settings_user_id
            FOREIGN KEY (user_id) REFERENCES users(user_id) ON UPDATE CASCADE ON DELETE CASCADE NOT VALID;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_el_reports_user_id') THEN
        ALTER TABLE el_reports ADD CONSTRAINT fk_el_reports_user_id
            FOREIGN KEY (user_id) REFERENCES users(user_id) ON UPDATE CASCADE ON DELETE CASCADE NOT VALID;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_el_report_groups_user_id') THEN
        ALTER TABLE el_report_groups ADD CONSTRAINT fk_el_report_groups_user_id
            FOREIGN KEY (user_id) REFERENCES users(user_id) ON UPDATE CASCADE ON DELETE CASCADE NOT VALID;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_el_pinned_reports_user_id') THEN
        ALTER TABLE el_pinned_reports ADD CONSTRAINT fk_el_pinned_reports_user_id
            FOREIGN KEY (user_id) REFERENCES users(user_id) ON UPDATE CASCADE ON DELETE CASCADE NOT VALID;
    END IF;
END $$;

-- The eight foreign keys above are NOT VALID on purpose.
--
-- NOT VALID enforces the constraint on every new and updated row immediately, but skips the scan
-- of existing rows. A validating ADD CONSTRAINT seq-scans the child under SHARE ROW EXCLUSIVE,
-- inside this file's transaction, during app startup. el_artifact_drops is 38M rows and 25 GB, so
-- that is minutes of blocked writes on boot, repeated from scratch if anything later in the file
-- fails.
--
-- UNTIL THE STATEMENTS BELOW ARE RUN, THESE CONSTRAINTS HOLD FOR NEW AND UPDATED ROWS ONLY.
-- The existing rows are unproven: Postgres trusts the constraint without having checked the data.
--
-- VALIDATE CONSTRAINT takes only SHARE UPDATE EXCLUSIVE and blocks neither reads nor writes, so it
-- is safe to run against production at any time. Run them one at a time; the two large tables take
-- a while.
--
--   ALTER TABLE el_mission        VALIDATE CONSTRAINT fk_el_mission_user_id;
--   ALTER TABLE el_backup         VALIDATE CONSTRAINT fk_el_backup_user_id;
--   ALTER TABLE el_mission_fuel   VALIDATE CONSTRAINT fk_el_mission_fuel_user_id;
--   ALTER TABLE el_settings       VALIDATE CONSTRAINT fk_el_settings_user_id;
--   ALTER TABLE el_reports        VALIDATE CONSTRAINT fk_el_reports_user_id;
--   ALTER TABLE el_report_groups  VALIDATE CONSTRAINT fk_el_report_groups_user_id;
--   ALTER TABLE el_pinned_reports VALIDATE CONSTRAINT fk_el_pinned_reports_user_id;
--   ALTER TABLE el_artifact_drops VALIDATE CONSTRAINT fk_el_artifact_drops_user_id;
--
-- What is still outstanding:
--
--   SELECT conrelid::regclass AS table_name, conname
--   FROM pg_constraint
--   WHERE contype = 'f' AND NOT convalidated
--   ORDER BY conrelid::regclass::text;
--
-- A validation that fails means orphaned rows exist. Count them before deciding what to do,
-- substituting the table name:
--
--   SELECT COUNT(*) FROM el_mission t
--   WHERE NOT EXISTS (SELECT 1 FROM users u WHERE u.user_id = t.user_id);
