-- discord_id is a login method, not an identity. `identities(provider, subject)` has been the
-- record of it since migration 8, and users.user_id has been the only key since then. The
-- discord_id columns below survived as a shadow generation with their own index sets and were
-- never read back: the audit found 0-2 index scans on el_artifact_drops' ~1.5 GB of discord_id
-- indexes against 1183-7368 on the user_id equivalents, and 0/10/24 on el_mission's against
-- 21/9879/368602. Roughly 2.5 GB of dead index across the two.
--
-- Migration 7's discord_id-keyed foreign keys are already gone: migration 8 lines 78-85 dropped
-- all six plus sessions/blobs before repointing the primary keys to user_id. Verified against the
-- live database, which carries exactly four foreign keys, all on user_id. The DROP CONSTRAINT
-- statements below are therefore belt-and-braces for any database that somehow skipped that, and
-- are named exactly as migration 7 created them.

ALTER TABLE el_mission DROP CONSTRAINT IF EXISTS fk_el_mission_discord_id;
ALTER TABLE el_backup DROP CONSTRAINT IF EXISTS fk_el_backup_discord_id;
ALTER TABLE el_artifact_drops DROP CONSTRAINT IF EXISTS fk_el_artifact_drops_discord_id;
ALTER TABLE el_settings DROP CONSTRAINT IF EXISTS fk_el_settings_discord_id;
ALTER TABLE el_reports DROP CONSTRAINT IF EXISTS fk_el_reports_discord_id;
ALTER TABLE el_report_groups DROP CONSTRAINT IF EXISTS fk_el_report_groups_discord_id;
ALTER TABLE sessions DROP CONSTRAINT IF EXISTS sessions_discord_id_fkey;
ALTER TABLE blobs DROP CONSTRAINT IF EXISTS blobs_discord_id_fkey;

DROP INDEX IF EXISTS idx_el_drops_player;
DROP INDEX IF EXISTS idx_el_drops_player_artifact;
DROP INDEX IF EXISTS idx_el_drops_player_level;
DROP INDEX IF EXISTS idx_el_drops_player_rarity;
DROP INDEX IF EXISTS idx_el_mission_player;
DROP INDEX IF EXISTS idx_el_mission_player_ship;
DROP INDEX IF EXISTS idx_el_mission_player_ts;
DROP INDEX IF EXISTS idx_el_reports_account;

ALTER TABLE el_mission DROP COLUMN IF EXISTS discord_id;
ALTER TABLE el_backup DROP COLUMN IF EXISTS discord_id;
ALTER TABLE el_artifact_drops DROP COLUMN IF EXISTS discord_id;
ALTER TABLE el_settings DROP COLUMN IF EXISTS discord_id;
ALTER TABLE el_reports DROP COLUMN IF EXISTS discord_id;
ALTER TABLE el_report_groups DROP COLUMN IF EXISTS discord_id;
ALTER TABLE blobs DROP COLUMN IF EXISTS discord_id;
ALTER TABLE sessions DROP COLUMN IF EXISTS discord_id;

-- users.discord_id goes last, and only for rows whose subject is already recorded in identities.
-- Migration 8 backfilled identities from users.discord_id, but a row inserted since then by a path
-- that wrote users.discord_id without writing identities would lose the linkage silently. Backfill
-- again, then fail loudly if anything is still unaccounted for rather than dropping the only copy.
INSERT INTO identities (user_id, provider, subject)
SELECT user_id, 'discord', discord_id FROM users
WHERE discord_id IS NOT NULL AND discord_id <> ''
ON CONFLICT (provider, subject) DO NOTHING;

DO $$
DECLARE
    unmigrated BIGINT;
BEGIN
    SELECT COUNT(*) INTO unmigrated
    FROM users u
    WHERE u.discord_id IS NOT NULL AND u.discord_id <> ''
      AND NOT EXISTS (
          SELECT 1 FROM identities i
          WHERE i.user_id = u.user_id AND i.provider = 'discord' AND i.subject = u.discord_id);
    IF unmigrated > 0 THEN
        RAISE EXCEPTION 'users.discord_id: % row(s) have no matching identities row, refusing to drop', unmigrated;
    END IF;
END $$;

DROP INDEX IF EXISTS idx_users_discord_id;
ALTER TABLE users DROP COLUMN IF EXISTS discord_id;
