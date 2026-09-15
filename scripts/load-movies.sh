#!/usr/bin/env bash
#
# Loads Resources/tmdb-movies.ndjson into the running Docker db using the psql
# loader (scripts/import-movies.sql + import-transform.sql). Data-only: the schema
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

"${COMPOSE[@]}" cp "$REPO_ROOT/Resources/tmdb-movies.ndjson" db:/tmp/tmdb-movies.ndjson
"${COMPOSE[@]}" cp "$REPO_ROOT/scripts/import-transform.sql" db:/tmp/import-transform.sql
"${COMPOSE[@]}" cp "$REPO_ROOT/scripts/import-movies.sql" db:/tmp/import-movies.sql
"${COMPOSE[@]}" exec -T db \
  sh -c 'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1 -f /tmp/import-movies.sql'
