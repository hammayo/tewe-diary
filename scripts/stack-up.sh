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

docker compose -f "$REPO_ROOT/docker-compose.yml" up -d --build

printf '\nSwagger UIs:\n'
printf '  Movies.Api:    http://localhost:5001/swagger  |  https://localhost:7001/swagger\n'
printf '  Identity.Api:  http://localhost:5003/swagger  |  https://localhost:7003/swagger\n'

# Movies data: current row count (same query as MovieRepository.GetCountAsync) plus
# a pointer to the loader. Retry briefly in case migrations are still running on boot.
printf '\nMovies data:\n'
movie_count=""
for _ in $(seq 1 10); do
  movie_count="$(docker compose -f "$REPO_ROOT/docker-compose.yml" exec -T db \
    sh -c 'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -tAc "select count(id) from movies"' 2>/dev/null | tr -d '[:space:]')"
  [[ "$movie_count" =~ ^[0-9]+$ ]] && break
  sleep 1
done
if [[ "$movie_count" =~ ^[0-9]+$ ]]; then
  printf '  Rows in movies table: %s\n' "$movie_count"
  if [[ "$movie_count" -eq 0 ]]; then
    printf '  -> Table is empty. Load sample data:  scripts/load-movies.sh\n'
  else
    printf '  (Re)load sample data:  scripts/load-movies.sh\n'
  fi
else
  printf '  Rows in movies table: unavailable (db starting or migrations pending)\n'
  printf '  Load sample data:  scripts/load-movies.sh\n'
fi
