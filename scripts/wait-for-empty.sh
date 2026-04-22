#!/usr/bin/env sh
# Watchtower pre-update hook for Deadlock game servers.
#
# Exits 0 (proceed with update) when either:
#   - the server is empty (player count == 0), or
#   - MAX_WAIT_SECONDS has elapsed since watchtower first asked to update.
#
# Otherwise exits non-zero so watchtower skips this cycle and retries on its
# next poll. The "first asked" timestamp is kept in tmpfs and persists across
# hook invocations for the life of the container — so the 30-minute cap spans
# multiple watchtower poll cycles, not a single hook invocation.

set -eu

COUNT_FILE=${PLAYER_COUNT_FILE:-/tmp/player_count}
SINCE_FILE=${UPDATE_SINCE_FILE:-/tmp/wt-update-since}
MAX_WAIT=${MAX_WAIT_SECONDS:-1800}

now=$(date +%s)

if [ ! -f "$SINCE_FILE" ]; then
    echo "$now" > "$SINCE_FILE"
fi
since=$(cat "$SINCE_FILE" 2>/dev/null || echo "$now")
case $since in
    ''|*[!0-9]*) since=$now ;;
esac
elapsed=$((now - since))

if [ -r "$COUNT_FILE" ]; then
    count=$(cat "$COUNT_FILE" 2>/dev/null || echo 0)
else
    count=0
fi
count=$(printf '%s' "$count" | tr -dc '0-9')
[ -z "$count" ] && count=0

if [ "$count" -eq 0 ]; then
    echo "wait-for-empty: 0 players — proceeding with update (waited ${elapsed}s)"
    rm -f "$SINCE_FILE"
    exit 0
fi

if [ "$elapsed" -ge "$MAX_WAIT" ]; then
    echo "wait-for-empty: ${count} players but ${elapsed}s >= ${MAX_WAIT}s — forcing update"
    rm -f "$SINCE_FILE"
    exit 0
fi

echo "wait-for-empty: ${count} players, ${elapsed}s/${MAX_WAIT}s waited — skipping this cycle"
exit 1
