#!/usr/bin/env bash
set -euo pipefail
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
GENRES="$(cat "$DIR/fixtures/genre-map.json")"

actual="$(jq -c --argjson genres "$GENRES" -f "$DIR/../tmdb-to-ndjson.jq" \
  "$DIR/fixtures/tmdb-popular-sample.json")"

if diff <(printf '%s\n' "$actual") "$DIR/fixtures/expected.ndjson"; then
  echo "PASS"
else
  echo "FAIL: output did not match expected.ndjson" >&2
  exit 1
fi
