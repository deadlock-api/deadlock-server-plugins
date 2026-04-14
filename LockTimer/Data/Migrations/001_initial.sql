CREATE TABLE IF NOT EXISTS zones (
    map        TEXT    NOT NULL,
    kind       INTEGER NOT NULL,
    min_x      REAL    NOT NULL,
    min_y      REAL    NOT NULL,
    min_z      REAL    NOT NULL,
    max_x      REAL    NOT NULL,
    max_y      REAL    NOT NULL,
    max_z      REAL    NOT NULL,
    updated_at INTEGER NOT NULL,
    PRIMARY KEY (map, kind)
);

CREATE TABLE IF NOT EXISTS records (
    steam_id    INTEGER NOT NULL,
    map         TEXT    NOT NULL,
    time_ms     INTEGER NOT NULL,
    player_name TEXT    NOT NULL,
    achieved_at INTEGER NOT NULL,
    PRIMARY KEY (steam_id, map)
);

CREATE INDEX IF NOT EXISTS idx_records_top ON records (map, time_ms);
