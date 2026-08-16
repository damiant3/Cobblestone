#!/bin/bash
# The plug-oracle-arith subject, run like a ladder rung. The subject is the
# repo's own codex/test/plug-oracle-arith.codex -- a single self-contained
# chapter, so there is no bundle step -- and the repo's .expected file is a
# third witness beside our banked truth.
set -e
T="$(cd "$(dirname "$0")/.." && pwd)"
REPO="$(cd "$T/.." && pwd)"
SUBJ=$REPO/codex/test/plug-oracle-arith.codex

cd $T/ast
python3 - <<PY
src = open('$SUBJ', 'rb').read()
open('arith-cdx.blob', 'wb').write(b"CDX map\n" + src + b"\x04")
open('arith-ir-cce.blob', 'wb').write(b"IR-CCE\n" + src + b"\x04")
print(f"blobs written ({len(src)} bytes of source)")
PY

cd $T
echo "--- compiling subject to a bare-metal binary"
rm -f ast/arith-subject.cdx ast/arith.ir
python3 -u ring_compile.py ast/arith-cdx.blob ast/arith-subject.cdx 2>&1 | tail -3
[ -s ast/arith-subject.cdx ] || { echo "COMPILE FAILED: no arith-subject.cdx"; exit 1; }

echo "--- compiling subject to IR-CCE for the plug"
python3 -u ring_compile.py ast/arith-ir-cce.blob ast/arith.ir 2>&1 | tail -3
[ -s ast/arith.ir ] || { echo "COMPILE FAILED: no arith.ir"; exit 1; }

echo "--- running the subject on bare metal"
python3 - <<PY
import codex_vm
out = codex_vm.run_cdx('ast/arith-subject.cdx', timeout=600, idle_timeout=120)
lines = [l for l in out.decode(errors='replace').splitlines()
         if not l.startswith(("WD:", "HEAP:", "STACK:"))]
open('ast/arith.truth', 'w').write("\n".join(lines) + "\n")
print(f"banked ast/arith.truth: {len(lines)} lines")
PY

cd ast
# The .expected is another battery's serial capture -- CRLF line ends and a
# leading 0x01 console byte -- so both sides shed control bytes before the
# compare, the same per-line trim build/plug-oracle-test.ps1 applies.
if ! diff <(tr -d '\r\001' < $REPO/codex/test/plug-oracle-arith.expected) <(tr -d '\r\001' < arith.truth); then
    echo "TRUTH DRIFT: bare metal disagrees with the repo's .expected"; exit 1
fi
echo "truth matches the repo's .expected"

cd $T
rm -f ast/arith.zig
python3 -u plug_run_checked.py \
    $REPO/codex/plugs/zig/build-output/zig-plug.cdx \
    ast/arith.ir ast/arith.zig
cd ast
if timeout 600 zig run arith.zig 2> arith.zigout; then
    if diff <(tr -d '\r' < arith.truth) arith.zigout > arith.diff 2>&1; then
        echo "ORACLE PASS: zig arith output byte-identical to bare-metal truth"
    else
        echo "ORACLE DIFF (first 15 lines):"
        head -15 arith.diff
        exit 1
    fi
else
    echo "--- zig compile/run errors:"
    head -40 arith.zigout
    exit 1
fi
