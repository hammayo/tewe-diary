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
#   scripts/reset-data.sh --reload         # after wiping: fetch the newest releases (500 pages, about 10,000
#                                          # movies, slow), then load every data/*.ndjson
#   scripts/reset-data.sh --reload --pages 50   # fetch only 50 pages (about 1,000 movies); implies --reload
#   scripts/reset-data.sh --no-fetch       # reload from the data/*.ndjson already on disk, no TMDB calls
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
FETCH=true
PAGES=""
ASSUME_YES=false
USAGE="Usage: scripts/reset-data.sh [--volume] [--reload] [--pages N | --no-fetch] [--yes]"
while [ $# -gt 0 ]; do
  case "$1" in
    --volume)   VOLUME=true; shift ;;
    --reload)   RELOAD=true; shift ;;
    --pages)    [ $# -ge 2 ] || { echo "$USAGE" >&2; exit 2; }; PAGES="$2"; RELOAD=true; shift 2 ;;
    --no-fetch) FETCH=false; RELOAD=true; shift ;;
    -y|--yes)   ASSUME_YES=true; shift ;;
    *) echo "$USAGE" >&2; exit 2 ;;
  esac
done

# Check the options before anything is wiped. TMDB serves at most 500 pages of 20 movies.
if [ -n "$PAGES" ]; then
  case "$PAGES" in ''|*[!0-9]*) echo "--pages needs a whole number from 1 to 500." >&2; exit 2 ;; esac
  { [ "$PAGES" -ge 1 ] && [ "$PAGES" -le 500 ]; } || { echo "--pages needs a whole number from 1 to 500." >&2; exit 2; }
  [ "$FETCH" = true ] || { echo "--pages and --no-fetch cannot be combined: --no-fetch downloads nothing." >&2; exit 2; }
fi
if [ "$FETCH" = false ] && ! compgen -G "$REPO_ROOT/data/*.ndjson" > /dev/null; then
  echo "--no-fetch has nothing to load: there is no data/*.ndjson. Run scripts/fetch-tmdb.sh first." >&2
  exit 2
fi

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
if [ "$RELOAD" = true ]; then
  if [ "$FETCH" = false ]; then
    echo "Afterwards it loads every data/*.ndjson already on disk, without fetching (files are kept)."
  else
    echo "Afterwards it fetches the newest releases (${PAGES:-500} pages) and loads every data/*.ndjson (files are kept)."
  fi
fi
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
  # Fetch the newest releases fresh (an existing file may predate the details fields), then load every
  # data/*.ndjson — so classics, per-year and by-id fetches from earlier come back too, not just the
  # newest set. The files themselves are never deleted by this script.
  # --pages N caps the fetch (20 movies a page); --no-fetch skips it and reloads the files already on disk.
  if [ "$FETCH" = true ]; then
    echo "Fetching the newest releases, then loading every data/*.ndjson..."
    "$SCRIPT_DIR/fetch-tmdb.sh" newest ${PAGES:+"$PAGES"}
  else
    echo "Loading every data/*.ndjson already on disk (no fetch)..."
  fi
  "$SCRIPT_DIR/load-movies.sh"
fi
