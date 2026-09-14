#!/usr/bin/env bash
#
# Runs the whole stack (Postgres + Identity.Api + Movies.Api) via Docker Compose.
# Movies.Api migrates the database on boot; Ctrl+C stops everything.
#
# Usage:
#   ./run.sh              # build + start the full stack in the foreground
#   ./run.sh -d           # start detached (background)
#   ./run.sh deps         # start only the dependencies (db + identity) for host debugging
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
COMPOSE_FILE="$SCRIPT_DIR/docker-compose.yml"

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

case "${1:-}" in
  -d|--detach)
    echo "Building and starting the full stack (detached)..."
    exec "${COMPOSE[@]}" -f "$COMPOSE_FILE" up --build -d
    ;;
  deps)
    # Just the dependencies, so Movies.Api can be run/debugged on the host separately.
    echo "Starting dependencies only (db + identity-api)..."
    exec "${COMPOSE[@]}" -f "$COMPOSE_FILE" up -d db identity-api
    ;;
  *)
    echo "Building and starting the full stack (db + identity-api + movies-api)..."
    exec "${COMPOSE[@]}" -f "$COMPOSE_FILE" up --build
    ;;
esac
