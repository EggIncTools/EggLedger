CREATE TABLE IF NOT EXISTS el_inflight_mission (
    user_id     UUID   NOT NULL,
    player_id   TEXT   NOT NULL,
    mission_id  TEXT   NOT NULL,
    captured_at BIGINT NOT NULL DEFAULT 0,
    payload     TEXT   NOT NULL DEFAULT '',
    PRIMARY KEY (user_id, player_id, mission_id)
);

CREATE INDEX IF NOT EXISTS idx_el_inflight_mission_user_player ON el_inflight_mission(user_id, player_id);

DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_el_inflight_mission_user_id') THEN
        ALTER TABLE el_inflight_mission
            ADD CONSTRAINT fk_el_inflight_mission_user_id FOREIGN KEY (user_id) REFERENCES users(user_id)
            ON DELETE CASCADE ON UPDATE CASCADE;
    END IF;
END $$;
