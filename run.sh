#!/usr/bin/env bash
#
# Runs the Movies API, ensuring the Postgres container is up and healthy first.
#
# Usage: ./run.sh
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
COMPOSE_FILE="$SCRIPT_DIR/docker-compose.yml"
API_PROJECT="$SCRIPT_DIR/Movies.Api"
DB_SERVICE="db"

# Prefer the docker compose plugin, fall back to the standalone docker-compose binary.
if docker compose version >/dev/null 2>&1; then
  COMPOSE=(docker compose)
elif command -v docker-compose >/dev/null 2>&1; then
  COMPOSE=(docker-compose)
else
  echo "Error: neither 'docker compose' nor 'docker-compose' is available on PATH." >&2
  exit 1
fi

if ! docker info >/dev/null 2>&1; then
  echo "Error: Docker daemon is not running. Start Docker and try again." >&2
  exit 1
fi

echo "Ensuring the '$DB_SERVICE' container is up..."
"${COMPOSE[@]}" -f "$COMPOSE_FILE" up -d "$DB_SERVICE"

CONTAINER_ID="$("${COMPOSE[@]}" -f "$COMPOSE_FILE" ps -q "$DB_SERVICE")"
if [ -z "$CONTAINER_ID" ]; then
  echo "Error: could not resolve the '$DB_SERVICE' container id." >&2
  exit 1
fi

echo "Waiting for the database to become healthy..."
ATTEMPTS=30
until [ "$(docker inspect -f '{{.State.Health.Status}}' "$CONTAINER_ID" 2>/dev/null)" = "healthy" ]; do
  ATTEMPTS=$((ATTEMPTS - 1))
  if [ "$ATTEMPTS" -le 0 ]; then
    echo "Error: database did not become healthy in time." >&2
    docker logs --tail 30 "$CONTAINER_ID" >&2 || true
    exit 1
  fi
  sleep 2
done

echo "Database is healthy. Starting the API..."
exec dotnet run --project "$API_PROJECT"
