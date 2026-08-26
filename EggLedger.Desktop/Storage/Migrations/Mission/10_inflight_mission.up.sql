CREATE TABLE IF NOT EXISTS inflight_mission (
    player_id   TEXT    NOT NULL,
    mission_id  TEXT    NOT NULL,
    captured_at INTEGER NOT NULL DEFAULT 0,
    payload     TEXT    NOT NULL DEFAULT '',
    PRIMARY KEY (player_id, mission_id)
);

CREATE INDEX IF NOT EXISTS idx_inflight_mission_player ON inflight_mission(player_id);
