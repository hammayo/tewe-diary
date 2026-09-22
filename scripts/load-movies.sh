#!/usr/bin/env bash
#
# Loads TMDB NDJSON (default Data/tmdb-movies.ndjson) into the running Docker db using the psql
# loader (scripts/helpers/import-movies.sql + import-transform.sql). Data-only: the schema
# is owned by the FluentMigrator migrations, never by this script.
#
# Requires the stack to be up (scripts/stack-up.sh). Safe to re-run: the transform
# upserts by id, skips slug collisions, and never downgrades already-enriched movies.
#
# The import is a single transaction, so there is no per-row count; progress shows the current step
# and elapsed time, then a summary (inserted/refreshed/skipped) and how many TMDB movies now have details.
#
# Usage:
#   scripts/load-movies.sh [path-to-ndjson]
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
COMPOSE=(docker compose -f "$REPO_ROOT/docker-compose.yml")
# shellcheck source=helpers/progress.sh
. "$SCRIPT_DIR/helpers/progress.sh"
# shellcheck source=helpers/db.sh
. "$SCRIPT_DIR/helpers/db.sh"

DATA="${1:-$REPO_ROOT/Data/tmdb-movies.ndjson}"
if [ ! -f "$DATA" ]; then
  echo "Error: $DATA not found. Fetch it first: scripts/fetch-tmdb.sh" >&2
  exit 1
fi

start=$SECONDS
lines="$(grep -c . "$DATA" || true)"
with_details="$(grep -c '"credits":' "$DATA" || true)"

# docker compose cp streams its progress UI to stderr; quiet it (set -e still
# aborts on a failed copy).
echo "[1/3] Copying $lines movies ($with_details with details) + loaders into the db container..."
"${COMPOSE[@]}" cp "$DATA" db:/tmp/tmdb-movies.ndjson 2>/dev/null
"${COMPOSE[@]}" cp "$SCRIPT_DIR/helpers/import-transform.sql" db:/tmp/import-transform.sql 2>/dev/null
"${COMPOSE[@]}" cp "$SCRIPT_DIR/helpers/import-movies.sql" db:/tmp/import-movies.sql 2>/dev/null

# psql prints per-statement command tags (BEGIN/COPY/INSERT/...) to stdout and the import NOTICE
# summary (and any error) to stderr. Run it in the background, keep stderr for the summary.
echo "[2/3] Importing in one transaction (stage, upsert movies/genres/metadata, details, credits)..."
log="$(mktemp)"
trap 'rm -f "$log"' EXIT
"${COMPOSE[@]}" exec -T db \
  sh -c 'psql -q -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1 -f /tmp/import-movies.sql' \
  >/dev/null 2>"$log" &

import_start=$SECONDS
import_progress() {
  printf "  running · %s elapsed   $1" "$(fmt_duration $((SECONDS - import_start)))" >&2
}
if ! watch_pid $! import_progress; then
  echo "Import failed; nothing was committed:" >&2
  cat "$log" >&2
  exit 1
fi
sed -n 's/.*NOTICE: *//p' "$log" | sed 's/^/  /'

read -r _ tmdb enriched _ <<<"$(db_counts)"
echo "[3/3] TMDB movies with details: $enriched / $tmdb"
echo "Done in $(fmt_duration $((SECONDS - start)))."
