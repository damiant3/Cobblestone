#!/bin/bash
# Run _try-sample-diag.sh over N samples in parallel, one line summary each.
# Usage: bash _try-samples-parallel.sh [-j N] <sample.codex> ...
# Default parallelism: 6.
set -uo pipefail
REPO="/mnt/d/Projects/NewRepository-cam"
SCRIPT="$REPO/tools/_try-sample-diag.sh"
J=6
if [ "${1:-}" = "-j" ]; then J="$2"; shift 2; fi

run_one() {
    local src="$1"
    local name=$(basename "$src" .codex)
    local out
    out=$(bash "$SCRIPT" "$src" 2>&1)
    local phs=$(echo "$out" | grep -oa 'PH:[a-z-]*' | head -20 | tr '\n' ',' | sed 's/,$//')
    local size=$(echo "$out" | grep -oa 'SIZE:[0-9]*' | head -1 | sed 's/SIZE://')
    if [ -n "$size" ]; then
        printf 'PASS  %-28s SIZE=%s\n' "$name" "$size"
    else
        local last=$(echo "$phs" | tr ',' '\n' | tail -1)
        printf 'FAIL  %-28s died-after=%s\n' "$name" "$last"
    fi
}
export -f run_one
export SCRIPT

printf '%s\n' "$@" | xargs -n1 -P"$J" -I{} bash -c 'run_one "$@"' _ {}
