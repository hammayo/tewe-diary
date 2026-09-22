#!/usr/bin/env bash
# Tests scripts/helpers/tmdb-details-to-ndjson.jq: credit trimming and trailer selection (PRD FR-5…FR-8).
set -euo pipefail
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
FILTER="$DIR/../helpers/tmdb-details-to-ndjson.jq"
failures=0

check() { # <name> <expected> <actual>
  if [ "$2" = "$3" ]; then
    echo "PASS  $1"
  else
    echo "FAIL  $1: expected '$2', got '$3'" >&2
    failures=$((failures + 1))
  fi
}

# One video object: key site type official name published_at
v() { printf '{"key":"%s","site":"%s","type":"%s","official":%s,"name":"%s","published_at":"%s"}' "$@"; }

# Runs the filter over a minimal details response wrapping the given videos array; prints the chosen key.
pick() {
  jq -c '{id: 1, title: "X", release_date: "2000-01-01", genres: [], videos: {results: .}}' <<<"$1" \
    | jq -c -f "$FILTER" | jq -r '.raw.videos.results[0].key // "none"'
}

# Fixture: 12 cast out of billing order, mixed crew, five videos.
line="$(jq -c -f "$FILTER" "$DIR/fixtures/tmdb-details-sample.json")"

check "top-level tmdb_id"                    "862"                          "$(jq -r '.tmdb_id' <<<"$line")"
check "genres from details shape"            '["Animation","Comedy"]'       "$(jq -c '.Genres' <<<"$line")"
check "scalar fields kept in raw"            "tt0114709|The adventure takes off!|81" \
                                             "$(jq -r '.raw | "\(.imdb_id)|\(.tagline)|\(.runtime)"' <<<"$line")"
check "cast trimmed to 10"                   "10"                           "$(jq -r '.raw.credits.cast | length' <<<"$line")"
check "cast in billing order"                "Actor 0|Actor 9"              "$(jq -r '.raw.credits.cast | "\(.[0].name)|\(.[-1].name)"' <<<"$line")"
check "crew keeps director + core writers"   "Director,Screenplay,Novel"    "$(jq -r '[.raw.credits.crew[].job] | join(",")' <<<"$line")"
check "videos trimmed to one"                "1"                            "$(jq -r '.raw.videos.results | length' <<<"$line")"
check "fixture trailer"                      "main"                         "$(jq -r '.raw.videos.results[0].key' <<<"$line")"

check "Official Trailer beats a newer recut" "main" "$(pick "[$(v recut YouTube Trailer true 'Official Countdown Trailer' 2026-02-01T00:00:00.000Z),$(v main YouTube Trailer true 'Official Trailer' 2025-12-01T00:00:00.000Z)]")"
check "Trailer beats a newer Teaser"         "trl"  "$(pick "[$(v tsr YouTube Teaser true 'Official Trailer' 2026-02-01T00:00:00.000Z),$(v trl YouTube Trailer true 'Trailer 2' 2025-01-01T00:00:00.000Z)]")"
check "official beats a newer unofficial"    "off"  "$(pick "[$(v fan YouTube Trailer false 'Official Trailer' 2026-02-01T00:00:00.000Z),$(v off YouTube Trailer true 'Trailer' 2025-01-01T00:00:00.000Z)]")"
check "newest wins among equals"             "new"  "$(pick "[$(v old YouTube Trailer true 'Trailer 1' 2025-01-01T00:00:00.000Z),$(v new YouTube Trailer true 'Trailer 2' 2025-06-01T00:00:00.000Z)]")"
check "Teaser fallback when no Trailer"      "t2"   "$(pick "[$(v t1 YouTube Teaser true 'Teaser 1' 2025-01-01T00:00:00.000Z),$(v t2 YouTube Teaser true 'Teaser 2' 2025-06-01T00:00:00.000Z),$(v clip YouTube Clip true 'Clip' 2026-01-01T00:00:00.000Z)]")"
check "Vimeo is supported"                   "vim"  "$(pick "[$(v vim Vimeo Trailer true 'Official Trailer' 2025-01-01T00:00:00.000Z)]")"
check "unsupported site gives no trailer"    "none" "$(pick "[$(v dm Dailymotion Trailer true 'Official Trailer' 2025-01-01T00:00:00.000Z)]")"
check "no videos gives no trailer"           "none" "$(pick "[]")"
check "missing release date is skipped"      ""     "$(jq -c -f "$FILTER" <<<'{"id":1,"title":"X","release_date":""}')"

if [ "$failures" -eq 0 ]; then
  echo "ALL PASS"
else
  echo "$failures check(s) failed" >&2
  exit 1
fi
