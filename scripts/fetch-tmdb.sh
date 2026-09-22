#!/usr/bin/env bash
#
# Fetches TMDB movies into NDJSON (one movie per line), with their details, credits and trailer.
#
# Presets (first word) — no TMDB knowledge needed:
#   scripts/fetch-tmdb.sh                     newest releases (the default)
#   scripts/fetch-tmdb.sh newest [pages]      newest releases, N pages of 20
#   scripts/fetch-tmdb.sh classics [minVotes] highest rated, at least minVotes votes (default 1000)
#   scripts/fetch-tmdb.sh popular             TMDB's popular list (also: top-rated, now-playing)
#   scripts/fetch-tmdb.sh trending [day|week] what's trending now (top 10)
#   scripts/fetch-tmdb.sh year 1994 [genre]   one year, optionally one or more genres
#   scripts/fetch-tmdb.sh genre "Horror,Sci"  one or more genres
#   scripts/fetch-tmdb.sh language hi         one original language (ISO 639-1)
#   scripts/fetch-tmdb.sh ids 680 550         exact TMDB ids (or: ids ids.txt)
#   scripts/fetch-tmdb.sh --help              this list
#
# A preset just sets the flags below, and you can still add flags after it, e.g.
#   scripts/fetch-tmdb.sh classics --pages 50 --min-rating 7
#
# Flags (all optional): --pages N --out PATH --year YYYY --genre "A,B" --original-language xx
#   --sort FIELD --min-rating X --min-votes N --list NAME --trending [day|week] --ids-file PATH
#   --dry-run (print what would be fetched, then stop)
#
# Each run writes Data/tmdb-<what-was-fetched>.ndjson (e.g. tmdb-discover-vote_average.desc-v1000.ndjson,
# tmdb-list-popular.ndjson, tmdb-trending-week.ndjson), so a new pull never overwrites an earlier one.
# Override with --out PATH. Load them all with scripts/load-movies.sh (duplicates are collapsed).
#
# Every mode is followed by an enrichment pass: one /movie/{id}?append_to_response=credits,videos
# call per movie (overview, tagline, runtime, imdb_id, top-10 cast, director/writers, one trailer).
#
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
# shellcheck source=helpers/progress.sh
. "$SCRIPT_DIR/helpers/progress.sh"

LIST=""
PAGES=500  # TMDB caps discover/list at page 500; requesting more returns HTTP 400
OUT=""   # default: Data/tmdb-<what-was-fetched>.ndjson (see default_out below)
YEAR=""
GENRE=""
ORIG_LANG=""
SORT=""
MIN_RATING=""
MIN_VOTES=""
TRENDING=""
IDS_FILE=""
IDS_LABEL=""      # names the output file when ids are given inline
PRESET=""         # the preset used, so the output file is named after it (not after TMDB's flags)
DRY_RUN=false

usage() { sed -n '2,/^$/p' "$0" | sed 's/^# \{0,1\}//'; }

# Presets: a friendly first word that just sets the flags below. Anything after it is parsed as usual.
case "${1:-}" in
  -h|--help) usage; exit 0 ;;
  newest)
    shift
    case "${1:-}" in ''|-*) ;; *) PAGES="$1"; shift ;; esac
    SORT="primary_release_date.desc"
    PRESET="newest" ;;
  classics)
    shift
    case "${1:-}" in ''|-*) MIN_VOTES=1000 ;; *) MIN_VOTES="$1"; shift ;; esac
    SORT="vote_average.desc"
    PRESET="classics" ;;
  popular|top-rated|now-playing)
    LIST="${1//-/_}"; PRESET="$1"; shift ;;
  trending)
    shift
    case "${1:-}" in day|week) TRENDING="$1"; shift ;; *) TRENDING="week" ;; esac
    PRESET="trending" ;;
  year)
    shift
    YEAR="${1:-}"; shift || true
    case "${1:-}" in ''|-*) ;; *) GENRE="$1"; shift ;; esac
    if [ -z "$YEAR" ]; then echo "Usage: scripts/fetch-tmdb.sh year YYYY [genre]" >&2; exit 2; fi
    PRESET="year" ;;
  genre)
    shift
    GENRE="${1:-}"; shift || true
    if [ -z "$GENRE" ]; then echo "Usage: scripts/fetch-tmdb.sh genre \"Horror,Thriller\"" >&2; exit 2; fi
    PRESET="genre" ;;
  language)
    shift
    ORIG_LANG="${1:-}"; shift || true
    if [ -z "$ORIG_LANG" ]; then echo "Usage: scripts/fetch-tmdb.sh language xx  (ISO 639-1)" >&2; exit 2; fi
    PRESET="language" ;;
  ids)
    shift
    if [ -f "${1:-}" ]; then
      IDS_FILE="$1"; shift
    else
      # Inline ids: stage them in a temp file, and name the output after the first few.
      IDS_FILE="$(mktemp)"
      trap 'rm -f "$IDS_FILE"' EXIT
      while [ $# -gt 0 ] && printf '%s' "$1" | grep -qE '^[0-9]+$'; do
        echo "$1" >> "$IDS_FILE"; shift
      done
      if [ ! -s "$IDS_FILE" ]; then
        echo "Usage: scripts/fetch-tmdb.sh ids 680 550   (or: ids ids.txt)" >&2; exit 2
      fi
      IDS_LABEL="$(head -3 "$IDS_FILE" | paste -sd- -)"
      [ "$(wc -l < "$IDS_FILE" | tr -d ' ')" -gt 3 ] && IDS_LABEL="$IDS_LABEL-and-more"
    fi
    PRESET="ids" ;;
esac

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
    --ids-file)          IDS_FILE="$2"; shift 2 ;;
    --dry-run)           DRY_RUN=true; shift ;;
    -h|--help)           usage; exit 0 ;;
    --trending)
      if [ "${2:-}" = "day" ] || [ "${2:-}" = "week" ]; then
        TRENDING="$2"; shift 2
      else
        TRENDING="week"; shift 1
      fi ;;
    *) echo "Unknown argument: $1" >&2; exit 2 ;;
  esac
done

# TMDB's discover/list endpoints return HTTP 400 beyond page 500. Clamp so an
# explicit --pages over the cap can't crash the run mid-fetch.
if [ "$PAGES" -gt 500 ]; then
  echo "Note: --pages $PAGES exceeds TMDB's max of 500; clamping to 500." >&2
  PAGES=500
fi

# Which mode do the flags request? Discover is triggered by any content/quality filter.
discover_requested=false
if [ -n "$YEAR" ] || [ -n "$GENRE" ] || [ -n "$ORIG_LANG" ] \
   || [ -n "$SORT" ] || [ -n "$MIN_RATING" ] || [ -n "$MIN_VOTES" ]; then
  discover_requested=true
fi

# Enforce mutual exclusivity between the modes.
if [ -n "$IDS_FILE" ]; then
  if [ -n "$TRENDING" ] || [ -n "$LIST" ] || [ "$discover_requested" = true ]; then
    echo "Error: --ids-file cannot be combined with --trending, --list or discover filters." >&2
    exit 2
  fi
  if [ ! -f "$IDS_FILE" ]; then
    echo "Error: --ids-file $IDS_FILE not found." >&2
    exit 2
  fi
  MODE="ids"
elif [ -n "$TRENDING" ]; then
  if [ -n "$LIST" ] || [ "$discover_requested" = true ]; then
    echo "Error: --trending cannot be combined with --list or discover filters (--year/--genre/...)." >&2
    exit 2
  fi
  MODE="trending"
elif [ "$discover_requested" = true ]; then
  if [ -n "$LIST" ]; then
    printf '%s\n' \
      "Error: --list uses TMDB's curated endpoints, which cannot be filtered." \
      "       To filter by --year/--genre/--original-language/..., use discover mode:" \
      "         popular    ->  --sort popularity.desc" \
      "         top_rated  ->  --sort vote_average.desc" \
      "       e.g. scripts/fetch-tmdb.sh --original-language en --sort popularity.desc --pages 100" >&2
    exit 2
  fi
  MODE="discover"
else
  # No --trending and no discover filters. Use --list if it was given, otherwise
  # default to a broad discover run (SORT/MIN_* defaults applied in the discover case).
  if [ -n "$LIST" ]; then
    MODE="list"
  else
    MODE="discover"
  fi
fi

# Default output file, named after what is being fetched, so a new pull never overwrites an earlier
# one (e.g. classics next to the newest releases). scripts/load-movies.sh loads them all together.
# A bare run (no preset, no filters) is exactly what `newest` does, so it gets that file.
if [ -z "$PRESET" ] && [ "$MODE" = discover ] && [ "$discover_requested" = false ]; then
  PRESET="newest"
fi

default_out() {
  local name
  # Named after the preset the caller used (tmdb-classics.ndjson), so the file matches the command.
  # Only a raw-flag run falls through to the mode+filters name.
  case "$PRESET" in
    newest)   printf '%s/Data/tmdb-newest.ndjson' "$ROOT_DIR"; return ;;
    classics) printf '%s/Data/tmdb-classics%s.ndjson' "$ROOT_DIR" \
                "$([ "${MIN_VOTES:-1000}" = 1000 ] || printf -- '-v%s' "$MIN_VOTES")"; return ;;
    popular|top-rated|now-playing)
              printf '%s/Data/tmdb-%s.ndjson' "$ROOT_DIR" "$PRESET"; return ;;
    trending) printf '%s/Data/tmdb-trending-%s.ndjson' "$ROOT_DIR" "$TRENDING"; return ;;
    year)     name="year-$YEAR${GENRE:+-$GENRE}" ;;
    genre)    name="genre-$GENRE" ;;
    language) name="language-$ORIG_LANG" ;;
    ids)      name="ids-${IDS_LABEL:-$(basename "${IDS_FILE%.*}")}" ;;
    *)
  case "$MODE" in
    list)     name="list-$LIST" ;;
    trending) name="trending-$TRENDING" ;;
    ids)      name="ids-${IDS_LABEL:-$(basename "${IDS_FILE%.*}")}" ;;
    discover)
      name="discover-${SORT:-primary_release_date.desc}"
      [ -n "$YEAR" ]        && name="$name-$YEAR"
      [ -n "$ORIG_LANG" ]   && name="$name-$ORIG_LANG"
      [ -n "$GENRE" ]       && name="$name-$GENRE"
      [ -n "$MIN_RATING" ]  && name="$name-r$MIN_RATING"
      [ -n "$MIN_VOTES" ]   && name="$name-v$MIN_VOTES"
      ;;
  esac ;;
  esac
  printf '%s/Data/tmdb-%s.ndjson' "$ROOT_DIR" \
    "$(printf '%s' "$name" | tr '[:upper:]' '[:lower:]' | sed 's/[^a-z0-9._-]/-/g; s/--*/-/g')"
}

if [ -z "$OUT" ]; then
  OUT="$(default_out)"
fi

if [ "$DRY_RUN" = true ]; then
  printf 'Would fetch: mode=%s' "$MODE" >&2
  [ -n "$LIST" ]       && printf ' list=%s' "$LIST" >&2
  [ -n "$TRENDING" ]   && printf ' window=%s' "$TRENDING" >&2
  [ -n "$IDS_FILE" ]   && printf ' ids=%s (%s)' "$IDS_FILE" "$(grep -c . "$IDS_FILE" || true)" >&2
  [ "$MODE" = discover ] && printf ' sort=%s year=%s genre=%s lang=%s min-rating=%s min-votes=%s pages=%s' \
    "${SORT:-primary_release_date.desc}" "${YEAR:-any}" "${GENRE:-any}" "${ORIG_LANG:-any}" \
    "${MIN_RATING:-6.5}" "${MIN_VOTES:-50}" "$PAGES" >&2
  printf '\nWould write:  %s\n' "$OUT" >&2
  exit 0
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
  AUTH_HEADER="Authorization: Bearer $TMDB_API_KEY"
  KEY_QS=""
else
  AUTH=(-H "accept: application/json")
  AUTH_HEADER=""
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
emit() { jq -c --argjson genres "$GENRE_MAP" -f "$SCRIPT_DIR/helpers/tmdb-to-ndjson.jq"; }

# One progress line for enrich(): done/total, %, failures, elapsed and a rough ETA. "Done" counts
# saved responses plus failed ids (a file in mid-download is counted early; at most 8 are in flight).
# Usage: enrich_progress <work-dir> <total> <start-seconds> <line-end: '\r' | '\n'>
enrich_progress() {
  local work="$1" total="$2" start="$3" end="$4" saved failed completed pct elapsed eta
  saved="$(find "$work" -name '*.json' | wc -l | tr -d ' ')"
  failed=0
  [ -f "$work/failed.txt" ] && failed="$(wc -l < "$work/failed.txt" | tr -d ' ')"
  completed=$((saved + failed))
  pct=$(( total > 0 ? completed * 100 / total : 100 ))
  elapsed=$((SECONDS - start))
  eta="--"
  if [ "$completed" -gt 0 ] && [ "$completed" -lt "$total" ]; then
    eta="$(fmt_duration $(( elapsed * (total - completed) / completed )))"
  elif [ "$completed" -ge "$total" ]; then
    eta="0s"
  fi
  printf "  %d/%d (%d%%) · %d failed · %s elapsed · ETA %s   ${end}" \
    "$completed" "$total" "$pct" "$failed" "$(fmt_duration "$elapsed")" "$eta" >&2
}

# Enrich every fetched movie with its details, credits and trailer (one /movie/{id} call each,
# 8 in parallel to stay under TMDB's rate limit), then rewrite $OUT in the details shape.
# Failed ids are skipped and reported, never fatal (PRD FR-10, D15).
enrich() {
  local work total ok
  work="$(mktemp -d)"
  jq -r '.tmdb_id' "$OUT" | awk '!seen[$0]++' > "$work/ids.txt"
  total="$(wc -l < "$work/ids.txt" | tr -d ' ')"
  echo "Enriching $total movies (details, credits, trailer)..." >&2

  # Fetch in the background and report progress while it runs (see watch_pid in helpers/progress.sh).
  TMDB_API="$API" TMDB_AUTH_HEADER="$AUTH_HEADER" TMDB_KEY_QS="$KEY_QS" \
    xargs -P 8 -n 1 "$SCRIPT_DIR/helpers/fetch-tmdb-detail.sh" "$work" < "$work/ids.txt" &
  watch_pid $! enrich_progress "$work" "$total" "$SECONDS"

  find "$work" -name '*.json' -print0 \
    | xargs -0 -r jq -c -f "$SCRIPT_DIR/helpers/tmdb-details-to-ndjson.jq" > "$OUT"
  ok="$(wc -l < "$OUT" | tr -d ' ')"
  echo "Enriched $ok/$total movies into $OUT" >&2
  if [ -s "$work/failed.txt" ]; then
    echo "Skipped $(wc -l < "$work/failed.txt" | tr -d ' ') movie(s) whose details call failed: $(sort -n "$work/failed.txt" | paste -sd, -)" >&2
  fi
  rm -rf "$work"
}

mkdir -p "$(dirname "$OUT")"
: > "$OUT"

case "$MODE" in
  list)
    for ((page=1; page<=PAGES; page++)); do
      printf '\rFetching %s: page %d/%d (%d movies)' "$LIST" "$page" "$PAGES" "$(wc -l < "$OUT")" >&2
      response="$(curl -fsSL "${AUTH[@]}" "$API/movie/$LIST?language=en-US&page=$page${KEY_QS}")"
      printf '%s' "$response" | emit >> "$OUT"
      if [ "$(printf '%s' "$response" | jq '.results | length')" -eq 0 ]; then
        printf '\nNo more results after page %d; stopping early.\n' "$page" >&2
        break
      fi
    done
    printf '\n' >&2
    echo "Wrote $(wc -l < "$OUT") movies to $OUT (list=$LIST, pages=$PAGES)"
    ;;
  discover)
    SORT="${SORT:-primary_release_date.desc}"
    MIN_RATING="${MIN_RATING:-6.5}"
    MIN_VOTES="${MIN_VOTES:-50}"
    for ((page=1; page<=PAGES; page++)); do
      printf '\rFetching: page %d/%d (%d movies)' "$page" "$PAGES" "$(wc -l < "$OUT")" >&2
      url="$API/discover/movie?language=en-US&include_adult=false&sort_by=$SORT&page=$page"
      url="$url&vote_average.gte=$MIN_RATING&vote_count.gte=$MIN_VOTES"
      [ -n "$YEAR" ]        && url="$url&primary_release_year=$YEAR"
      [ -n "$WITH_GENRES" ] && url="$url&with_genres=$WITH_GENRES"
      [ -n "$ORIG_LANG" ]   && url="$url&with_original_language=$ORIG_LANG"
      url="$url$KEY_QS"
      response="$(curl -fsSL "${AUTH[@]}" "$url")"
      printf '%s' "$response" | emit >> "$OUT"
      # TMDB keeps serving empty pages past the last result; stop instead of burning requests.
      if [ "$(printf '%s' "$response" | jq '.results | length')" -eq 0 ]; then
        printf '\nNo more results after page %d; stopping early.\n' "$page" >&2
        break
      fi
    done
    printf '\n' >&2
    echo "Wrote $(wc -l < "$OUT") movies to $OUT (discover: year=${YEAR:-any}, genre=${GENRE:-any}, lang=${ORIG_LANG:-any}, sort=$SORT, min-rating=$MIN_RATING, min-votes=$MIN_VOTES, pages=$PAGES)"
    ;;
  ids)
    # No list call: turn the ids into minimal NDJSON rows; enrich() below fetches their details.
    grep -E '^[0-9]+$' "$IDS_FILE" | awk '{ print "{\"tmdb_id\":" $1 "}" }' > "$OUT"
    echo "Read $(wc -l < "$OUT" | tr -d ' ') TMDB ids from $IDS_FILE"
    ;;
  trending)
    # /trending returns results already sorted by trend; take the top 10.
    # Filter to a temp file first so closing the pipe early can't SIGPIPE curl/jq under pipefail.
    printf 'Fetching trending (%s)...\n' "$TRENDING" >&2
    tmp="$(mktemp)"
    curl -fsSL "${AUTH[@]}" "$API/trending/movie/$TRENDING?language=en-US${KEY_QS}" | emit > "$tmp"
    head -n 10 "$tmp" > "$OUT"
    rm -f "$tmp"
    echo "Wrote $(wc -l < "$OUT") movies to $OUT (trending=$TRENDING, top 10)"
    ;;
esac

enrich
