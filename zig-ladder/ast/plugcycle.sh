#!/bin/bash
# Rebuild the zig plug from ZigEmitter.codex, push one milestone's banked IR
# through it, and report either the oracle verdict or an inventory of what
# zig rejected.  Usage: plugcycle.sh <milestone>
#
# The two counts are not the same signal and the second is the honest one.
# zig stops at the first @compileError, so the errors it PRINTS under-report;
# the markers are counted in the emitted source instead, where every gap the
# emitter refused to guess at is still visible.
set -e
. "$(dirname "$0")/oracle_lib.sh"
m=${1:?usage: plugcycle.sh <milestone>}

"$T/cycle.sh"
echo "REBUILT"

zig_arm "$m" && exit 0

cd "$T/ast"
echo "errors: $(grep -c ': error: ' ${m}.zigout || true)  markers-in-source: $(grep -o 'zig plug: [^\"]*' ${m}.zig | wc -l)"
echo "--- what zig rejected"
grep -o 'error: .*' ${m}.zigout | sed 's/^error: //' | cut -c1-64 | sort | uniq -c | sort -rn
echo "--- markers the emitter left in ${m}.zig"
grep -o 'zig plug: [^\"]*' ${m}.zig | sort | uniq -c | sort -rn
exit 1
