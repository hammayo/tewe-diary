#!/usr/bin/env bash
# reset-data.sh must reject bad options before it touches the database or the stack. Each case below
# exits at argument checking, so nothing is wiped and no container is needed.
set -uo pipefail
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SCRIPT="$DIR/../reset-data.sh"
failed=0

expect_exit_2() {
  local name="$1"; shift

  # Act
  "$SCRIPT" "$@" >/dev/null 2>&1 </dev/null
  local code=$?

  # Assert
  if [ "$code" -eq 2 ]; then
    echo "PASS: $name"
  else
    echo "FAIL: $name (expected exit 2, got $code)" >&2
    failed=1
  fi
}

# Arrange: nothing to set up, the cases pass options only.
expect_exit_2 "unknown option"                 --bogus
expect_exit_2 "--pages without a value"        --pages
expect_exit_2 "--pages not a number"           --pages abc
expect_exit_2 "--pages zero"                   --pages 0
expect_exit_2 "--pages above TMDB's 500 cap"   --pages 501
expect_exit_2 "--pages with --no-fetch"        --pages 5 --no-fetch

exit "$failed"
