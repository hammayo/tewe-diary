-- Run inside the db container after copying the NDJSON + transform into /tmp:
--   docker cp Resources/tmdb-movies.ndjson  <db>:/tmp/tmdb-movies.ndjson
--   docker cp scripts/import-transform.sql  <db>:/tmp/import-transform.sql
--   docker cp scripts/import-movies.sql     <db>:/tmp/import-movies.sql
--   docker compose exec -T db psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" \
--       -v ON_ERROR_STOP=1 -f /tmp/import-movies.sql
begin;

create temp table _import (doc jsonb) on commit drop;
-- Read each NDJSON line verbatim into one jsonb cell. CSV format is used (not the
-- default TEXT format) because TEXT treats backslash as an escape and would strip
-- JSON's \" and \\ sequences, corrupting any line containing quotes. QUOTE and
-- DELIMITER are set to control bytes that never occur in JSON, so no field or quote
-- processing happens and the whole line is taken as-is.
\copy _import(doc) from '/tmp/tmdb-movies.ndjson' with (format csv, quote E'\x01', delimiter E'\x02')

\i /tmp/import-transform.sql

commit;
