#!/usr/bin/env bash
#
# Wipes the local movie data for a clean start: every movie (TMDB and manually created), its genres,
# details, credits, metadata and ALL ratings. There is no undo. The schema is owned by the
# FluentMigrator migrations and is never edited here.
#
# Requires the stack to be up (scripts/stack-up.sh), except with --volume.
#
# Usage:
#   scripts/reset-data.sh                  # empty every data table (schema and container kept)
#   scripts/reset-data.sh --volume         # also delete the Postgres volume, then restart the stack
#   scripts/reset-data.sh --reload         # after wiping: fetch fresh TMDB data and load it
#   scripts/reset-data.sh --volume --reload
#   scripts/reset-data.sh --yes ...        # skip the confirmation prompt (for scripting)
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
# shellcheck source=helpers/db.sh
. "$SCRIPT_DIR/helpers/db.sh"
# shellcheck source=helpers/lock.sh
. "$SCRIPT_DIR/helpers/lock.sh"

VOLUME=false
RELOAD=false
ASSUME_YES=false
while [ $# -gt 0 ]; do
  case "$1" in
    --volume)   VOLUME=true; shift ;;
    --reload)   RELOAD=true; shift ;;
    -y|--yes)   ASSUME_YES=true; shift ;;
    *) echo "Usage: scripts/reset-data.sh [--volume] [--reload] [--yes]" >&2; exit 2 ;;
  esac
done

# Refuse to run while another script is starting or resetting the stack (a second `docker compose up`
# fails with "container name is already in use"). stack-up.sh, called below for --volume, inherits it.
stack_lock

# What is about to be destroyed (best effort: the db may be down, or not migrated yet).
if counts="$(db_counts 2>/dev/null)"; then
  read -r movies tmdb _ credits _ _ _ _ _ _ _ <<<"$counts"
  ratings="$(db_query <<<'select count(*) from ratings;' 2>/dev/null || echo '?')"
  echo "This deletes $movies movies ($tmdb from TMDB, $((movies - tmdb)) manual), $credits credits and $ratings ratings."
else
  echo "This deletes every movie, genre, rating, metadata, details and credits row (db not reachable, so no counts)."
fi
$VOLUME && echo "It also deletes the Postgres volume, then restarts the stack."
$RELOAD && echo "Afterwards it fetches fresh TMDB data and loads it."
echo "There is no undo."

if [ "$ASSUME_YES" != true ]; then
  read -r -p "Type 'wipe' to continue: " answer
  [ "$answer" = "wipe" ] || { echo "Cancelled."; exit 1; }
fi

if [ "$VOLUME" = true ]; then
  echo "Removing containers and the Postgres volume..."
  docker compose -f "$REPO_ROOT/docker-compose.yml" down -v
  echo "Restarting the stack (migrations run on boot)..."
  "$SCRIPT_DIR/stack-up.sh"
else
  # movies is the parent of genres/ratings/movie_metadata/movie_details/movie_credits, so one
  # `truncate ... cascade` empties them all (the same statement the test fixture uses).
  echo "Emptying the data tables..."
  db_query <<<'truncate movies cascade;' > /dev/null
  echo "Done. The schema is untouched."
fi

if [ "$RELOAD" = true ]; then
  # A fresh fetch, not the existing file: the old NDJSON may predate the details fields.
  "$SCRIPT_DIR/fetch-tmdb.sh"
  "$SCRIPT_DIR/load-movies.sh"
fi
