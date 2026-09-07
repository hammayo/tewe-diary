# TMDB Import Runbook

Populate `movies`, `genres`, and `movie_metadata` from TMDB. The `ratings`
table is never touched. Imports are idempotent (deduped on `tmdb_id`), so
re-running only adds movies not already imported.

## 1. Configure

Add your TMDB v3 API key to `.env` (gitignored):

    TMDB_API_KEY=<your key>

Get a key at https://www.themoviedb.org/settings/api.

## 2. Fetch (on demand)

    scripts/fetch-tmdb.sh --list popular --pages 5
    # writes Resources/tmdb-movies.ndjson

Lists: `popular`, `top_rated`, `now_playing`. 20 movies per page. Override the
output path with `--out PATH`.

## 3. Import into the running db container

The db runs in Docker and no host `psql` client is required. Copy the NDJSON,
the transform, and the runner into the container, then run the runner with psql
inside the container:

    # load POSTGRES_* from .env into the shell
    set -a; . ./.env; set +a

    CID=$(docker compose ps -q db)
    docker cp Resources/tmdb-movies.ndjson "$CID":/tmp/tmdb-movies.ndjson
    docker cp scripts/import-transform.sql "$CID":/tmp/import-transform.sql
    docker cp scripts/import-movies.sql    "$CID":/tmp/import-movies.sql

    docker compose exec -T db \
      psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1 \
      -f /tmp/import-movies.sql

The schema (including `movie_metadata`) is created automatically the first time
`Movies.Api` starts (`DbInitializer`); ensure the API has run at least once, or
the tables already exist, before importing.

Re-running fetch + import refreshes with newly-seen movies only; movies already
present (same `tmdb_id`) are skipped. The `ratings` table is never modified.
