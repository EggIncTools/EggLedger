CREATE TABLE IF NOT EXISTS mission_fuel (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    mission_id TEXT    NOT NULL,
    player_id  TEXT    NOT NULL,
    fuel_index INTEGER NOT NULL,
    egg_id     INTEGER NOT NULL,
    amount     REAL    NOT NULL DEFAULT 0,
    UNIQUE(mission_id, player_id, fuel_index)
);

CREATE INDEX IF NOT EXISTS idx_mission_fuel_player ON mission_fuel(player_id);
