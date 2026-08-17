#!/bin/bash
# Rebuild the plug once, then run EVERY milestone oracle against it.
# The ladder only means anything if the lower rungs stay green: an emitter
# change made for one phase reaches all of them, and a milestone that
# passed yesterday proves nothing about the plug built today.
set -e
. "$(dirname "$0")/oracle_lib.sh"
"$T/cycle.sh"
# Both plugs come from the same ZigEmitter, and a sweep that refreshed
# only one would report yesterday's emitter for whichever rungs use the
# other.
bash "$T/ast/ringplug_build.sh" > /dev/null
echo "REBUILT"
fail=0
for m in lex parse desugar scope check lower text pingpong lir fib fibx scale whole; do
    echo "=== $m ==="
    # NOT `arm | head`: under a pipe the exit status is head's, so a
    # failing rung reports nothing and the sweep still says it passed.
    out=$($(arm_for "$m") "$m" 2>&1) || fail=1
    printf '%s\n' "$out" | head -14
    # A rung that says nothing is not a rung that passed. fibx once
    # exited 0 with its verdict truncated away, which reads identically
    # to a rung that never ran.
    case "$out" in
        *ORACLE*|*TRANSPORT\ FAILED*) ;;
        *) echo "NO VERDICT from $m -- treating as failure"; fail=1 ;;
    esac
done
exit $fail
