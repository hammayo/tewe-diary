#!/usr/bin/env bash
# Fetches one movie's details (+credits, +videos) from TMDB into <dir>/<id>.json. Called by
# fetch-tmdb.sh via `xargs -P 8`. A failed call (404, or retries exhausted on 429/5xx) is
# recorded in <dir>/failed.txt and never fails the run (PRD D15).
# Usage: fetch-tmdb-detail.sh <dir> <tmdb_id>
# Env:   TMDB_API (base url), TMDB_AUTH_HEADER (may be empty), TMDB_KEY_QS (may be empty)
set -uo pipefail
dir="$1"; id="$2"
url="$TMDB_API/movie/$id?language=en-US&append_to_response=credits,videos&include_video_language=en,null$TMDB_KEY_QS"
args=(-fsSL --retry 5 --retry-delay 2 -H "accept: application/json")
if [ -n "$TMDB_AUTH_HEADER" ]; then
  args+=(-H "$TMDB_AUTH_HEADER")
fi
if ! curl "${args[@]}" -o "$dir/$id.json" "$url"; then
  rm -f "$dir/$id.json"
  echo "$id" >> "$dir/failed.txt"
fi
exit 0
