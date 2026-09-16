-- Finishes the time-representation work migration 19 deliberately stopped short of.
--
-- 19 converted only the columns the server owns outright. These five tables are different: they
-- round-trip through JsonRowCodec, which is shared with the desktop host's SqliteIndexedDb, and
-- SQLite has no timestamptz. The conversion is absorbed at the Postgres boundary instead, so the
-- JSON wire format stays numeric epoch seconds and both hosts still agree:
--
--   read  - JsonRowCodec.WriteColumn maps DateTime/DateTimeOffset back to an epoch number
--   write - PostgresIndexedDb marks these columns as epoch and converts on the way in
--
-- The typed row records in Rows.cs keep their long/double fields and the desktop schema is
-- untouched, so existing desktop clients are unaffected. el_backup.backed_up_at is double
-- precision rather than bigint, which is why the codec preserves sub-second values instead of
-- truncating to whole seconds.

ALTER TABLE el_backup
    ALTER COLUMN backed_up_at TYPE TIMESTAMPTZ USING to_timestamp(backed_up_at);

ALTER TABLE el_inflight_mission
    ALTER COLUMN captured_at DROP DEFAULT;
ALTER TABLE el_inflight_mission
    ALTER COLUMN captured_at TYPE TIMESTAMPTZ USING to_timestamp(captured_at);
ALTER TABLE el_inflight_mission
    ALTER COLUMN captured_at SET DEFAULT now();

ALTER TABLE el_reports
    ALTER COLUMN created_at TYPE TIMESTAMPTZ USING to_timestamp(created_at);
ALTER TABLE el_reports
    ALTER COLUMN updated_at TYPE TIMESTAMPTZ USING to_timestamp(updated_at);

ALTER TABLE el_report_groups
    ALTER COLUMN created_at DROP DEFAULT;
ALTER TABLE el_report_groups
    ALTER COLUMN created_at TYPE TIMESTAMPTZ USING to_timestamp(created_at);
ALTER TABLE el_report_groups
    ALTER COLUMN created_at SET DEFAULT now();

ALTER TABLE el_pinned_reports
    ALTER COLUMN created_at TYPE TIMESTAMPTZ USING to_timestamp(created_at);
