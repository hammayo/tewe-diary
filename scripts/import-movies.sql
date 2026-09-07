-- Run inside the db container after copying the NDJSON + transform into /tmp:
--   docker cp Resources/tmdb-movies.ndjson  <db>:/tmp/tmdb-movies.ndjson
--   docker cp scripts/import-transform.sql  <db>:/tmp/import-transform.sql
--   docker cp scripts/import-movies.sql     <db>:/tmp/import-movies.sql
--   docker compose exec -T db psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" \
--       -v ON_ERROR_STOP=1 -f /tmp/import-movies.sql
begin;

create temp table _import (doc jsonb) on commit drop;
\copy _import(doc) from '/tmp/tmdb-movies.ndjson'

\i /tmp/import-transform.sql

commit;
