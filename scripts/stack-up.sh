#!/usr/bin/env bash
#
# Brings up the full Docker stack (db + identity-api + movies-api) detached,
# then prints the Swagger URLs once the containers are started.
#
# Ensures the ASP.NET dev cert used for HTTPS in the containers exists
# (.certs/aspnet-dev.pfx), generating it on first run if needed.
#
# Usage:
#   scripts/stack-up.sh            # build + start detached, then show URLs
#
# Used by the Rider "Docker Stack" run configuration (.run/Docker Stack.run.xml)
# and safe to run directly from a shell.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
# shellcheck source=helpers/db.sh
. "$SCRIPT_DIR/helpers/db.sh"
# shellcheck source=helpers/lock.sh
. "$SCRIPT_DIR/helpers/lock.sh"

# One `docker compose up` at a time: a second one (e.g. this script while reset-data.sh --volume is
# recreating the stack) fails with "container name is already in use".
stack_lock

CERT_DIR="$REPO_ROOT/.certs"
CERT_FILE="$CERT_DIR/aspnet-dev.pfx"

# Password must match ASPNETCORE_DEV_CERT_PASSWORD in .env (compose reads it there).
CERT_PASSWORD="$(grep -E '^ASPNETCORE_DEV_CERT_PASSWORD=' "$REPO_ROOT/.env" 2>/dev/null | cut -d= -f2- | tr -d '\r')"
CERT_PASSWORD="${CERT_PASSWORD:-devcert}"

clear

if [[ ! -f "$CERT_FILE" ]]; then
  echo "Generating ASP.NET dev cert at .certs/aspnet-dev.pfx ..."
  mkdir -p "$CERT_DIR"
  dotnet dev-certs https -ep "$CERT_FILE" -p "$CERT_PASSWORD" --format Pfx
  echo "Tip: run 'dotnet dev-certs https --trust' once to avoid browser warnings on the https URLs."
fi

docker compose -f "$REPO_ROOT/docker-compose.yml" up -d --build --remove-orphans

printf '\nSwagger UIs:\n'
printf '  Movies.Api:    http://localhost:5001/swagger  |  https://localhost:7001/swagger\n'
printf '  Identity.Api:  http://localhost:5003/swagger  |  https://localhost:7003/swagger\n'

# Movies data: counts with a breakdown, when TMDB data was last imported, the scripts in the order
# they're used (fetch -> load, or the enrich shortcut), and a next step only when one is needed.
# Retry briefly while migrations run on boot (the query fails until they have).
printf '\nMovies data:\n'
counts=""
for _ in $(seq 1 10); do
  counts="$(db_counts 2>/dev/null)" || counts=""
  [[ "$counts" =~ ^[0-9]+( [0-9]+){9} ]] && break
  sleep 1
done

# pct <part> <whole> -> "n%" (0% when whole is 0)
pct() { if [ "$2" -gt 0 ]; then printf '%d%%' $(($1 * 100 / $2)); else printf '0%%'; fi; }

movies=""
if [[ "$counts" =~ ^[0-9]+( [0-9]+){9} ]]; then
  read -r movies tmdb enriched credits posters trailers taglines directors writers cast last_import <<<"$counts"
  printf '  Movies    %-7s %s from TMDB · %s manual\n' "$movies" "$tmdb" "$((movies - tmdb))"
  printf '  Details   %-7s of %s TMDB movies (%s) · posters %s · trailers %s · taglines %s\n' \
    "$enriched" "$tmdb" "$(pct "$enriched" "$tmdb")" "$posters" "$trailers" "$taglines"
  printf '  Credits   %-7s %s directors · %s writers · %s cast\n' "$credits" "$directors" "$writers" "$cast"
  printf '  Imported  %s\n' "${last_import:-never}"
else
  printf '  Counts unavailable (db starting or migrations pending).\n'
fi

printf '\n  Scripts (in order, all safe to re-run):\n'
printf '    1. scripts/fetch-tmdb.sh [options]     one file per fetch -> Data/tmdb-<what-was-fetched>.ndjson\n'
printf '         (no options) or newest [pages]      newest releases            -> tmdb-newest.ndjson\n'
printf '         classics [minVotes]                 highest rated, 1000+ votes -> tmdb-classics.ndjson\n'
printf '         year 1994 [genre]                   one year, optional genres  -> tmdb-year-1994.ndjson\n'
printf '         genre Horror / language hi          by genre or language\n'
printf '         popular | top-rated | now-playing   TMDB curated lists\n'
printf '         trending [day|week]                 trending now (top 10)\n'
printf '         ids 680 550                         exact TMDB ids (--help for all)\n'
printf '    2. scripts/load-movies.sh [files...]   load every Data/*.ndjson (or just the files you name);\n'
printf '                                           duplicates across files are collapsed on tmdb_id\n'
printf '    Or, for movies already in the db (fetches, then loads):\n'
printf '       scripts/enrich-movies.sh [--all]    fill in missing details (--all: refresh every TMDB movie)\n'
printf '    Start over (deletes every movie and rating, asks first):\n'
printf '       scripts/reset-data.sh [--volume] [--reload]   wipe the data (--volume: the db volume too;\n'
printf '                                                     --reload: then fetch + load fresh TMDB data)\n'

if [[ -n "$movies" ]]; then
  if [[ "$movies" -eq 0 ]]; then
    printf '\n  -> Empty. Run 1 then 2.\n'
  elif [[ "$enriched" -lt "$tmdb" ]]; then
    printf '\n  -> %s TMDB movies are missing details. Run: scripts/enrich-movies.sh\n' "$((tmdb - enriched))"
  fi
fi
