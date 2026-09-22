# Progress helpers shared by the ops scripts (sourced, not executed).

# 75 -> "1m15s", 3700 -> "1h01m40s".
fmt_duration() {
  local s="$1"
  if [ "$s" -ge 3600 ]; then printf '%dh%02dm%02ds' $((s / 3600)) $((s % 3600 / 60)) $((s % 60))
  elif [ "$s" -ge 60 ]; then printf '%dm%02ds' $((s / 60)) $((s % 60))
  else printf '%ds' "$s"
  fi
}

# Reports on a background job until it exits: runs `<print-fn> [args...] <line-end>` every second on a
# terminal (line-end '\r', redrawn in place) or every 30s otherwise (line-end '\n', e.g. CI logs), then
# once more with '\n' when the job ends. It polls every second, so it returns as soon as the job does.
# Returns the job's exit status.
# Usage: watch_pid <pid> <print-fn> [args...]
watch_pid() {
  local pid="$1" interval end next=$SECONDS status=0
  shift
  if [ -t 2 ]; then interval=1; end='\r'; else interval=30; end='\n'; fi
  while kill -0 "$pid" 2>/dev/null; do
    if [ "$SECONDS" -ge "$next" ]; then
      "$@" "$end"
      next=$((SECONDS + interval))
    fi
    sleep 1
  done
  wait "$pid" || status=$?
  "$@" '\n'
  return "$status"
}
