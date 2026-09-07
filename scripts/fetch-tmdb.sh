#!/usr/bin/env bash
#
# Fetches TMDB movies into NDJSON (one movie per line).
#
# Modes (mutually exclusive):
#   List (default):  --list popular|top_rated|now_playing            -> /movie/{list}
#   Discover:        any of --year/--genre/--original-language/      -> /discover/movie
#                    --sort/--min-rating/--min-votes
#   Trending:        --trending [day|week]  (top 10)                 -> /trending/movie/{window}
#
# Usage:
#   scripts/fetch-tmdb.sh [--list popular|top_rated|now_playing] [--pages N] [--out PATH]
#   scripts/fetch-tmdb.sh --year 2024 [--genre "Action,Comedy"] [--original-language en] \
#                         [--sort primary_release_date.desc] [--min-rating 6.5] \
#                         [--min-votes 50] [--pages N] [--out PATH]
#   scripts/fetch-tmdb.sh --trending [day|week] [--out PATH]
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"

LIST=""
PAGES=5
OUT="$ROOT_DIR/Resources/tmdb-movies.ndjson"
YEAR=""
GENRE=""
ORIG_LANG=""
SORT=""
MIN_RATING=""
MIN_VOTES=""
TRENDING=""

while [ $# -gt 0 ]; do
  case "$1" in
    --list)              LIST="$2"; shift 2 ;;
    --pages)             PAGES="$2"; shift 2 ;;
    --out)               OUT="$2"; shift 2 ;;
    --year)              YEAR="$2"; shift 2 ;;
    --genre)             GENRE="$2"; shift 2 ;;
    --original-language) ORIG_LANG="$2"; shift 2 ;;
    --sort)              SORT="$2"; shift 2 ;;
    --min-rating)        MIN_RATING="$2"; shift 2 ;;
    --min-votes)         MIN_VOTES="$2"; shift 2 ;;
    --trending)
      if [ "${2:-}" = "day" ] || [ "${2:-}" = "week" ]; then
        TRENDING="$2"; shift 2
      else
        TRENDING="week"; shift 1
      fi ;;
    *) echo "Unknown argument: $1" >&2; exit 2 ;;
  esac
done

# Which mode do the flags request? Discover is triggered by any content/quality filter.
discover_requested=false
if [ -n "$YEAR" ] || [ -n "$GENRE" ] || [ -n "$ORIG_LANG" ] \
   || [ -n "$SORT" ] || [ -n "$MIN_RATING" ] || [ -n "$MIN_VOTES" ]; then
  discover_requested=true
fi

# Enforce mutual exclusivity between the three modes.
if [ -n "$TRENDING" ]; then
  if [ -n "$LIST" ] || [ "$discover_requested" = true ]; then
    echo "Error: --trending cannot be combined with --list or discover filters (--year/--genre/...)." >&2
    exit 2
  fi
  MODE="trending"
elif [ "$discover_requested" = true ]; then
  if [ -n "$LIST" ]; then
    echo "Error: --list cannot be combined with discover filters (--year/--genre/...)." >&2
    exit 2
  fi
  MODE="discover"
else
  MODE="list"
  LIST="${LIST:-popular}"
fi

# Load .env if present so TMDB_API_KEY is available for local runs.
if [ -f "$ROOT_DIR/.env" ]; then
  set -a; . "$ROOT_DIR/.env"; set +a
fi

if [ -z "${TMDB_API_KEY:-}" ]; then
  echo "Error: TMDB_API_KEY is not set (add it to .env)." >&2
  exit 1
fi

API="https://api.themoviedb.org/3"

# TMDB accepts either a v3 API key (passed as the ?api_key= query parameter) or a
# v4 read-access token (passed as an Authorization: Bearer header). v4 tokens are
# JWTs containing dots; anything else is treated as a v3 key. Sending a v3 key as a
# Bearer token returns HTTP 401, so pick the scheme from the key's shape.
if printf '%s' "$TMDB_API_KEY" | grep -q '\.'; then
  AUTH=(-H "Authorization: Bearer $TMDB_API_KEY" -H "accept: application/json")
  KEY_QS=""
else
  AUTH=(-H "accept: application/json")
  KEY_QS="&api_key=$TMDB_API_KEY"
fi

# Build the genre id -> name map once. Used by the NDJSON transform, and to
# translate --genre names into TMDB genre ids for discover mode.
GENRE_MAP="$(curl -fsSL "${AUTH[@]}" "$API/genre/movie/list?language=en-US${KEY_QS}" \
  | jq -c '[.genres[] | {(.id|tostring): .name}] | add')"

# Translate a comma-separated list of genre names into a %7C-joined (OR) id list.
WITH_GENRES=""
if [ -n "$GENRE" ]; then
  IFS=',' read -ra _genre_names <<< "$GENRE"
  for _name in "${_genre_names[@]}"; do
    _name="$(printf '%s' "$_name" | sed 's/^[[:space:]]*//;s/[[:space:]]*$//')"
    [ -z "$_name" ] && continue
    _id="$(printf '%s' "$GENRE_MAP" \
      | jq -r --arg n "$_name" 'to_entries[] | select(.value | ascii_downcase == ($n | ascii_downcase)) | .key')"
    if [ -z "$_id" ]; then
      echo "Error: unknown genre '$_name'. Valid genres:" >&2
      printf '%s' "$GENRE_MAP" | jq -r '.[]' | sort | sed 's/^/  - /' >&2
      exit 2
    fi
    WITH_GENRES="${WITH_GENRES:+$WITH_GENRES%7C}$_id"
  done
fi

# Transform a TMDB list response (stdin) into NDJSON on stdout.
emit() { jq -c --argjson genres "$GENRE_MAP" -f "$SCRIPT_DIR/tmdb-to-ndjson.jq"; }

mkdir -p "$(dirname "$OUT")"
: > "$OUT"

case "$MODE" in
  list)
    for ((page=1; page<=PAGES; page++)); do
      curl -fsSL "${AUTH[@]}" "$API/movie/$LIST?language=en-US&page=$page${KEY_QS}" | emit >> "$OUT"
    done
    echo "Wrote $(wc -l < "$OUT") movies to $OUT (list=$LIST, pages=$PAGES)"
    ;;
  discover)
    SORT="${SORT:-primary_release_date.desc}"
    MIN_RATING="${MIN_RATING:-6.5}"
    MIN_VOTES="${MIN_VOTES:-50}"
    for ((page=1; page<=PAGES; page++)); do
      url="$API/discover/movie?language=en-US&include_adult=false&sort_by=$SORT&page=$page"
      url="$url&vote_average.gte=$MIN_RATING&vote_count.gte=$MIN_VOTES"
      [ -n "$YEAR" ]        && url="$url&primary_release_year=$YEAR"
      [ -n "$WITH_GENRES" ] && url="$url&with_genres=$WITH_GENRES"
      [ -n "$ORIG_LANG" ]   && url="$url&with_original_language=$ORIG_LANG"
      url="$url$KEY_QS"
      curl -fsSL "${AUTH[@]}" "$url" | emit >> "$OUT"
    done
    echo "Wrote $(wc -l < "$OUT") movies to $OUT (discover: year=${YEAR:-any}, genre=${GENRE:-any}, lang=${ORIG_LANG:-any}, sort=$SORT, min-rating=$MIN_RATING, min-votes=$MIN_VOTES, pages=$PAGES)"
    ;;
  trending)
    # /trending returns results already sorted by trend; take the top 10.
    # Filter to a temp file first so closing the pipe early can't SIGPIPE curl/jq under pipefail.
    tmp="$(mktemp)"
    curl -fsSL "${AUTH[@]}" "$API/trending/movie/$TRENDING?language=en-US${KEY_QS}" | emit > "$tmp"
    head -n 10 "$tmp" > "$OUT"
    rm -f "$tmp"
    echo "Wrote $(wc -l < "$OUT") movies to $OUT (trending=$TRENDING, top 10)"
    ;;
esac
