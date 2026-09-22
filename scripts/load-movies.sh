#!/usr/bin/env bash
#
# Loads TMDB NDJSON into the running Docker db using the psql
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
#   scripts/load-movies.sh                      # every Data/*.ndjson (all fetches so far)
#   scripts/load-movies.sh file.ndjson [more...] # only these
#
# Several files are staged together in one transaction, so a movie appearing in more than one file is
# imported once (deduped on tmdb_id); movies already in the db are refreshed in place.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
COMPOSE=(docker compose -f "$REPO_ROOT/docker-compose.yml")
# shellcheck source=helpers/progress.sh
. "$SCRIPT_DIR/helpers/progress.sh"
# shellcheck source=helpers/db.sh
. "$SCRIPT_DIR/helpers/db.sh"

if [ $# -gt 0 ]; then
  FILES=("$@")
else
  FILES=("$REPO_ROOT"/Data/*.ndjson)
fi

for f in "${FILES[@]}"; do
  if [ ! -f "$f" ]; then
    echo "Error: $f not found. Fetch first: scripts/fetch-tmdb.sh" >&2
    exit 1
  fi
done

start=$SECONDS

# One staging file for all inputs: the transform dedupes on tmdb_id, so the same movie in two files is
# imported once, and the newest line for it wins within the batch.
staged="$(mktemp)"
trap 'rm -f "$staged"' EXIT
for f in "${FILES[@]}"; do
  printf '  %-56s %6s movies (%s with details)\n' "$(basename "$f")" \
    "$(grep -c . "$f" || true)" "$(grep -c '"credits":' "$f" || true)"
  cat "$f" >> "$staged"
done
lines="$(grep -c . "$staged" || true)"
unique="$(jq -r '.tmdb_id' "$staged" 2>/dev/null | sort -u | wc -l | tr -d ' ')"

# docker compose cp streams its progress UI to stderr; quiet it (set -e still
# aborts on a failed copy).
echo "[1/3] Copying ${#FILES[@]} file(s), $lines lines ($unique unique movies) + loaders into the db container..."
"${COMPOSE[@]}" cp "$staged" db:/tmp/tmdb-movies.ndjson 2>/dev/null
"${COMPOSE[@]}" cp "$SCRIPT_DIR/helpers/import-transform.sql" db:/tmp/import-transform.sql 2>/dev/null
"${COMPOSE[@]}" cp "$SCRIPT_DIR/helpers/import-movies.sql" db:/tmp/import-movies.sql 2>/dev/null

# psql prints per-statement command tags (BEGIN/COPY/INSERT/...) to stdout and the import NOTICE
# summary (and any error) to stderr. Run it in the background, keep stderr for the summary.
echo "[2/3] Importing in one transaction (stage, upsert movies/genres/metadata, details, credits)..."
log="$(mktemp)"
trap 'rm -f "$staged" "$log"' EXIT
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
