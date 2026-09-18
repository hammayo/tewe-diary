#!/usr/bin/env bash
#
# Loads Data/tmdb-movies.ndjson into the running Docker db using the psql
# loader (scripts/helpers/import-movies.sql + import-transform.sql). Data-only: the schema
# is owned by the FluentMigrator migrations, never by this script.
#
# Requires the stack to be up (scripts/stack-up.sh). Safe to re-run — the transform
# upserts by id and skips slug collisions.
#
# Usage:
#   scripts/load-movies.sh
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
COMPOSE=(docker compose -f "$REPO_ROOT/docker-compose.yml")

# docker compose cp streams its progress UI to stderr; quiet it (set -e still
# aborts on a failed copy).
echo "Copying data + loaders into the db container..."
"${COMPOSE[@]}" cp "$REPO_ROOT/Data/tmdb-movies.ndjson" db:/tmp/tmdb-movies.ndjson 2>/dev/null
"${COMPOSE[@]}" cp "$SCRIPT_DIR/helpers/import-transform.sql" db:/tmp/import-transform.sql 2>/dev/null
"${COMPOSE[@]}" cp "$SCRIPT_DIR/helpers/import-movies.sql" db:/tmp/import-movies.sql 2>/dev/null

# psql prints per-statement command tags (BEGIN/COPY/INSERT/...) to stdout and
# the import NOTICE summary to stderr. Drop stdout, keep the summary.
echo "Running import..."
"${COMPOSE[@]}" exec -T db \
  sh -c 'psql -q -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1 -f /tmp/import-movies.sql' >/dev/null

echo "Done."
