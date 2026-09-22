#!/usr/bin/env bash
#
# Fills in TMDB details (overview, tagline, runtime, poster/backdrop, credits, trailer) for the TMDB
# movies already in the local Docker db. It reads their tmdb_ids from the db, fetches details for
# exactly those ids (scripts/fetch-tmdb.sh --ids-file), then loads the result (scripts/load-movies.sh).
# Movies are refreshed in place: same ids, ratings kept.
#
# Requires the stack to be up (scripts/stack-up.sh) and TMDB_API_KEY in .env.
#
# Usage:
#   scripts/enrich-movies.sh          # only movies still missing details
#   scripts/enrich-movies.sh --all    # re-fetch details for every TMDB movie
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
# shellcheck source=helpers/db.sh
. "$SCRIPT_DIR/helpers/db.sh"

case "${1:-}" in
  "")    filter="where not raw ? 'credits'" ;;
  --all) filter="" ;;
  *)     echo "Usage: scripts/enrich-movies.sh [--all]" >&2; exit 2 ;;
esac

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

db_query > "$work/ids.txt" <<SQL
select tmdb_id from movie_metadata $filter order by tmdb_id;
SQL
count="$(grep -c . "$work/ids.txt" || true)"

if [ "$count" -eq 0 ]; then
  echo "All TMDB movies already have details. Nothing to do."
  exit 0
fi

echo "Filling in details for $count TMDB movies (one TMDB call each)..."
"$SCRIPT_DIR/fetch-tmdb.sh" --ids-file "$work/ids.txt" --out "$work/enriched.ndjson"
"$SCRIPT_DIR/load-movies.sh" "$work/enriched.ndjson"
