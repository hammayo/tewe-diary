# Stack lock shared by the scripts that run `docker compose up` (sourced, not executed).
#
# Two concurrent compose runs (e.g. Rider's Docker Stack config while reset-data.sh --volume is
# recreating the stack) fail with "container name is already in use". stack_lock makes the second
# one stop with a clear message instead.

# Takes the lock for the current script, or exits 1 if another script holds it. The lock is released
# when the script exits (trap), and is inherited: a script that already holds it can call another
# locking script (reset-data.sh -> stack-up.sh) without deadlocking.
stack_lock() {
  # Already held by this process tree (exported below): nothing to do.
  [ "${TEWE_STACK_LOCK:-}" = "held" ] && return 0

  local lock_dir="${TMPDIR:-/tmp}/tewe-stack.lock"
  if ! mkdir "$lock_dir" 2>/dev/null; then
    local owner_pid owner_cmd
    owner_pid="$(cat "$lock_dir/pid" 2>/dev/null || true)"
    owner_cmd="$(cat "$lock_dir/cmd" 2>/dev/null || echo 'another stack script')"
    if [ -n "$owner_pid" ] && kill -0 "$owner_pid" 2>/dev/null; then
      echo "Error: $owner_cmd (pid $owner_pid) is already starting or resetting the stack." >&2
      echo "       Running two at once breaks with 'container name is already in use'." >&2
      echo "       Wait for it to finish, then run this again." >&2
      exit 1
    fi
    echo "Note: clearing a stale stack lock (pid ${owner_pid:-unknown} is gone)." >&2
    rm -rf "$lock_dir"
    mkdir "$lock_dir" || { echo "Error: could not create $lock_dir." >&2; exit 1; }
  fi

  echo "$$" > "$lock_dir/pid"
  basename "$0" > "$lock_dir/cmd"
  export TEWE_STACK_LOCK=held
  # shellcheck disable=SC2064  # expand lock_dir now: it is a local
  trap "rm -rf '$lock_dir'" EXIT
}
