#!/usr/bin/env bash
#
# Fetches a TMDB discovery list into NDJSON (one movie per line).
# Usage: scripts/fetch-tmdb.sh [--list popular|top_rated|now_playing] [--pages N] [--out PATH]
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"

LIST="popular"
PAGES=5
OUT="$ROOT_DIR/Resources/tmdb-movies.ndjson"

while [ $# -gt 0 ]; do
  case "$1" in
    --list)  LIST="$2"; shift 2 ;;
    --pages) PAGES="$2"; shift 2 ;;
    --out)   OUT="$2"; shift 2 ;;
    *) echo "Unknown argument: $1" >&2; exit 2 ;;
  esac
done

# Load .env if present so TMDB_API_KEY is available for local runs.
if [ -f "$ROOT_DIR/.env" ]; then
  set -a; . "$ROOT_DIR/.env"; set +a
fi

if [ -z "${TMDB_API_KEY:-}" ]; then
  echo "Error: TMDB_API_KEY is not set (add it to .env)." >&2
  exit 1
fi

API="https://api.themoviedb.org/3"
AUTH=(-H "Authorization: Bearer $TMDB_API_KEY" -H "accept: application/json")

# Build the genre id -> name map once.
GENRE_MAP="$(curl -fsSL "${AUTH[@]}" "$API/genre/movie/list?language=en-US" \
  | jq -c '[.genres[] | {(.id|tostring): .name}] | add')"

mkdir -p "$(dirname "$OUT")"
: > "$OUT"

for ((page=1; page<=PAGES; page++)); do
  curl -fsSL "${AUTH[@]}" "$API/movie/$LIST?language=en-US&page=$page" \
    | jq -c --argjson genres "$GENRE_MAP" -f "$SCRIPT_DIR/tmdb-to-ndjson.jq" \
    >> "$OUT"
done

echo "Wrote $(wc -l < "$OUT") movies to $OUT"
