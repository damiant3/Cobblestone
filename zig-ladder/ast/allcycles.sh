#!/bin/bash
# Rebuild the plug once, then run EVERY milestone oracle against it.
# The ladder only means anything if the lower rungs stay green: an emitter
# change made for one phase reaches all of them, and a milestone that
# passed yesterday proves nothing about the plug built today.
set -e
. "$(dirname "$0")/oracle_lib.sh"
"$T/cycle.sh"
echo "REBUILT"
fail=0
for m in lex parse desugar scope check lower text pingpong lir fib; do
    echo "=== $m ==="
    # NOT `zig_arm | head`: under a pipe the exit status is head's, so a
    # failing rung reports nothing and the sweep still says it passed.
    out=$(zig_arm "$m" 2>&1) || fail=1
    printf '%s\n' "$out" | head -14
done
exit $fail
